using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using System;

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

        // The name of the database and container we will create
        private string databaseId = "Analytics";
        private string containerId = "ViewEvents";

        public DataStorage(string endpointURI, string cosmosKey )
        {
            // Create a new instance of the Cosmos Client
            this.cosmosClient = new CosmosClient(endpointURI, cosmosKey, new CosmosClientOptions() { ApplicationName = "AnalyticsWebhooks" });
        }

        public async Task Init()
        {
            await this.CreateDatabaseAsync();
            await this.CreateContainerAsync();
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

        // <CreateContainerAsync>
        /// <summary>
        /// Create the container if it does not exist. 
        /// Specifiy "/partitionKey" as the partition key path since we're storing family information, to ensure good distribution of requests and storage.
        /// </summary>
        /// <returns></returns>
        private async Task CreateContainerAsync()
        {
            // Create a new container
            this.container = await this.database.CreateContainerIfNotExistsAsync(containerId, "/day");
            Console.WriteLine("Created Container: {0}\n", this.container.Id);
        }
        // </CreateContainerAsync>
        public async Task CreateRequestItem(RequestRecord request)
        {
            await this.container.CreateItemAsync<RequestRecord>(request, new PartitionKey(request.day));
        }
    }
}
