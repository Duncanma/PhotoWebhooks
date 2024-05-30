using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.JsonPatch.Internal;

namespace PhotoWebhooks
{

    internal class DataStorage
    {
        // The Cosmos client instance
        private CosmosClient cosmosClient;

        // The database we will create
        private Database database;

        // The container we will create.
        private Container container;
        private Container c_viewsByDate;
        private Container c_viewsByPathByDate;

        // The name of the database and container we will create
        private string databaseId = "Analytics";
        private string containerId = "ViewEvents";
        private string viewsByDate = "ViewsByDate";
        private string viewsByPath = "ViewsByPathByDate";

        public DataStorage(string endpointURI, string cosmosKey )
        {
            // Create a new instance of the Cosmos Client
            this.cosmosClient = new CosmosClient(endpointURI, cosmosKey, new CosmosClientOptions() { ApplicationName = "AnalyticsWebhooks" });
        }

        public async Task Init()
        {
            await this.CreateDatabaseAsync();
            await this.CreateContainersAsync();
        }

        private async Task CreateContainersAsync()
        {
            // Create a new container
            this.container = await this.database.CreateContainerIfNotExistsAsync(containerId, "/day");
            // Create a new container
            this.c_viewsByDate = await this.database.CreateContainerIfNotExistsAsync(viewsByDate, "/dateType");
            // Create a new container
            this.c_viewsByPathByDate = await this.database.CreateContainerIfNotExistsAsync(viewsByPath, "/dateType");

        }

        // <CreateDatabaseAsync>
        /// <summary>
        /// Create the database if it does not exist
        /// </summary>
        private async Task CreateDatabaseAsync()
        {
            // Create a new database
            this.database = await this.cosmosClient.CreateDatabaseIfNotExistsAsync(databaseId);
            Console.WriteLine("Created Database: {0}\n", this.database.Id);
        }
        // </CreateDatabaseAsync>

        public async Task CreateRequestItem(RequestRecord request)
        {
            await this.container.CreateItemAsync<RequestRecord>(request, new PartitionKey(request.day));
        }

        public async Task CreateViewByDate(ViewsByDate viewByDate)
        {
            await this.c_viewsByDate.CreateItemAsync<ViewsByDate>(viewByDate, new PartitionKey(viewByDate.dateType));
        }

        public async Task CreateViewByPathByDate(ViewsByPathByDate viewsByPathByDate)
        {
            await this.c_viewsByPathByDate.CreateItemAsync<ViewsByPathByDate>(viewsByPathByDate, new PartitionKey(viewsByPathByDate.dateType));
        }

        public async Task<Dictionary<string, string>> GetViewsByDay(int days)
        {
            Dictionary<string, string> results = new Dictionary<string, string>();

            string query = "SELECT c.dateType,c.id,c.views FROM c WHERE c.id > @firstDay ORDER by c.id";
            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@firstDay", DateTimeOffset.Now.AddDays(days).ToString("yyyyMMdd"));

            using (FeedIterator<ViewsByDate> feedIterator = this.c_viewsByDate.GetItemQueryIterator<ViewsByDate>(
                q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        results.Add(item.id, item.views);
                    }
                }
            }

            return results;
        }

        public async Task GetAndSaveViewsByDate(string dateType)
        {
            string query = "";
            if (dateType == "day") {
                query = "SELECT 'day' as dateType, c.day as id, count(1) as views FROM c GROUP BY c.day ORDER by c.day offset 0 limit 5";
            }

            using (FeedIterator<ViewsByDate> feedIterator = this.container.GetItemQueryIterator<ViewsByDate>(
                query))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        await this.c_viewsByDate.UpsertItemAsync(item);
                    }
                }
            }
        }


        public async Task GetAndSaveViewsByPathByDate(string dateType)
        {
            string query = "";
            if (dateType == "day")
            {
                query = "SELECT @dateType as dateType, c.day as id, c.page, count(1) as views FROM c WHERE c.day > @firstDay GROUP BY c.day, c.page ORDER BY c.day desc";
            }

            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", dateType)
                .WithParameter("@firstDay", DateTimeOffset.Now.AddDays(-1).ToString("yyyyMMdd"));
            
            using (FeedIterator<ViewsByPathByDate> feedIterator = this.container.GetItemQueryIterator<ViewsByPathByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByPathByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        await this.c_viewsByPathByDate.UpsertItemAsync(item);
                    }
                }
            }
        }


    }
}
