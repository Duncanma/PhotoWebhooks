using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoWebhooks
{

    internal class OrderData
    {
        internal class LogItem
        {
            internal LogItem() {
                DateTimeOffset requestTime = DateTimeOffset.UtcNow;
                Guid uniqueString = Guid.NewGuid();
                id = $"{requestTime:yyyyMMdd':'HH':'mm':'ss'.'fffffff}::{uniqueString}";
            }
            public string id { get; }
            public string function { get; set; }
            public string checkoutSessionID { get; set; }
            public string eventID { get; set; }
            public bool duplicate { get; set; }
        }

        // The Cosmos client instance
        private CosmosClient cosmosClient;

        // The database we will create
        private Database database;

        // The container we will create.
        private Container c_functionLog;

        // The name of the database and container we will create
        private string databaseId = "Analytics";
        private string functionLog = "FunctionLog";

        public OrderData(string endpointURI, string cosmosKey )
        {
            // Create a new instance of the Cosmos Client
            this.cosmosClient = new CosmosClient(endpointURI, cosmosKey, new CosmosClientOptions() { ApplicationName = "OrderWebhooks" });
        }

        public async Task Init()
        {
            await this.CreateDatabaseAsync();
            await this.CreateContainersAsync();
        }

        private async Task CreateContainersAsync()
        {
            // Create a new container
            this.c_functionLog = await this.database.CreateContainerIfNotExistsAsync(functionLog, "/function");

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

        public async Task insertLogItem(string functionName, 
            string sessionID, string eventID, bool duplicate)
        {
            LogItem item = new LogItem() 
            { 
                function = functionName, 
                checkoutSessionID = sessionID, 
                eventID = eventID, 
                duplicate = duplicate 
            };

            await this.c_functionLog.CreateItemAsync<LogItem>(
                item, new PartitionKey(functionName));
        }

        public async Task<Boolean> checkForExisting(string functionName, 
            string checkoutSessionID)
        {
            string query = "SELECT c.id " +
                "FROM c WHERE c.checkoutSessionID =" + 
                "@checkoutSessionID AND c.function=@functionName";

            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@checkoutSessionID", checkoutSessionID)
                .WithParameter("@functionName", functionName);

            using (FeedIterator<LogItem> feedIterator 
                = this.c_functionLog.GetItemQueryIterator<LogItem>(q))
            {
                if (feedIterator.HasMoreResults)
                {
                    FeedResponse<LogItem> response
                        = await feedIterator.ReadNextAsync();

                    if (response != null && response.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
