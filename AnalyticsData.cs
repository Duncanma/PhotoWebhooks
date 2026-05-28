using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoWebhooks
{

    internal class AnalyticsData
    {
        private CosmosClient cosmosClient;
        private Database database;
        private Container c_viewEvents;
        private Container c_viewsByDate;
        private Container c_viewsByPathByDate;
        private Container c_viewsByReferrerByDate;
        private Container c_viewsByCountryByDate;

        private string databaseId = "Analytics";
        private string viewEvents = "ViewEvents";
        private string viewsByDate = "ViewsByDate";
        private string viewsByPath = "ViewsByPathByDate";
        private string viewsByReferrer = "ViewsByReferrerByDate";
        private string viewsByCountry = "ViewsByCountryByDate";

        public AnalyticsData(string endpointURI, string cosmosKey )
        {
            this.cosmosClient = new CosmosClient(endpointURI, cosmosKey, new CosmosClientOptions() { ApplicationName = "AnalyticsWebhooks" });
        }

        public async Task Init()
        {
            await this.CreateDatabaseAsync();
            await this.CreateContainersAsync();
        }

        private async Task CreateContainersAsync()
        {
            this.c_viewEvents = await this.database.CreateContainerIfNotExistsAsync(viewEvents, "/day");
            this.c_viewsByDate = await this.database.CreateContainerIfNotExistsAsync(viewsByDate, "/dateType");
            this.c_viewsByPathByDate = await this.database.CreateContainerIfNotExistsAsync(viewsByPath, "/dateType");
            this.c_viewsByReferrerByDate = await this.database.CreateContainerIfNotExistsAsync(viewsByReferrer, "/dateType");
            this.c_viewsByCountryByDate = await this.database.CreateContainerIfNotExistsAsync(viewsByCountry, "/dateType");
        }

        private async Task CreateDatabaseAsync()
        {
            this.database = await this.cosmosClient.CreateDatabaseIfNotExistsAsync(databaseId);
            Console.WriteLine("Created Database: {0}\n", this.database.Id);
        }

        public async Task CreateRequestItem(RequestRecord request)
        {
            await this.c_viewEvents.CreateItemAsync<RequestRecord>(request, new PartitionKey(request.day));
        }

        public async Task<Dictionary<string, string>> GetViewsByDay(int days)
        {
            Dictionary<string, string> results 
                = new Dictionary<string, string>();

            string query = "SELECT c.dateType,c.id,c.views " +
                "FROM c WHERE c.dateType = 'day' AND c.id > @firstDay ORDER by c.id";

            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@firstDay", 
                DateTimeOffset.Now.AddDays(days).ToString("yyyyMMdd"));

            using (FeedIterator<ViewsByDate> feedIterator 
                = this.c_viewsByDate.GetItemQueryIterator<ViewsByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByDate> response 
                        = await feedIterator.ReadNextAsync();

                    foreach (var item in response)
                    {
                        results.Add(item.id, item.views);
                    }
                }
            }

            return results;
        }

        public async Task GetAndSaveViewsByDate(string dateType, bool allTime = false)
        {
            string query = "";
            if (dateType == "day") {
                
                if (allTime)
                {
                    query = "SELECT @dateType as dateType, c.day as id, " +
                        "count(1) as views FROM c WHERE c.isSpider = false GROUP BY c.day " +
                        "ORDER by c.day";
                }
                else
                {
                    query = "SELECT @dateType as dateType, c.day as id, " +
                        "count(1) as views FROM c WHERE c.day > @firstDay AND c.isSpider = false GROUP BY c.day " +
                        "ORDER by c.day offset 0 limit 8";
                }
            }
            QueryDefinition q;
            
            if (allTime)
            {
                q = new QueryDefinition(query)
                .WithParameter("@dateType", "day");
            } else {
                q = new QueryDefinition(query)
                .WithParameter("@dateType", "day")
                .WithParameter("@firstDay", DateTimeOffset.Now.AddDays(-8).ToString("yyyyMMdd"));

            }

            int count = 0;
            using (FeedIterator<ViewsByDate> feedIterator 
                = this.c_viewEvents.GetItemQueryIterator<ViewsByDate>(
                q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByDate> response 
                        = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        Console.WriteLine(item.id);
                        count++;
                        await this.c_viewsByDate.UpsertItemAsync(item, new PartitionKey(item.dateType));
                    }
                }
            }
            Console.WriteLine($"{count} records written");
        }


        public async Task GetAndSaveViewsByPathByDate(string dateType)
        {
            string query = "";
            if (dateType == "day")
            {
                query = "SELECT @dateType as dateType, c.day, c.page, count(1) as views FROM c WHERE c.day > @firstDay AND c.isSpider = false GROUP BY c.day, c.page ORDER BY c.day desc";
            }

            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", dateType)
                .WithParameter("@firstDay", DateTimeOffset.Now.AddDays(-2).ToString("yyyyMMdd"));
            
            using (FeedIterator<ViewsByPathByDate> feedIterator = this.c_viewEvents.GetItemQueryIterator<ViewsByPathByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByPathByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        item.id = item.page.Replace("/", "--") + "::" + item.day;
                        await this.c_viewsByPathByDate.UpsertItemAsync(item, new PartitionKey(item.dateType));
                    }
                }
            }
        }

        public async Task GetAndSaveViewsByReferrerByDate(string dateType)
        {
            if (dateType != "day") return;

            string query = "SELECT @dateType as dateType, c.day, c.simple_referrer as referrer, count(1) as views " +
                "FROM c WHERE c.day > @firstDay AND c.isSpider = false AND IS_DEFINED(c.simple_referrer) AND c.simple_referrer != null " +
                "GROUP BY c.day, c.simple_referrer ORDER BY c.day desc";

            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", dateType)
                .WithParameter("@firstDay", DateTimeOffset.Now.AddDays(-2).ToString("yyyyMMdd"));

            using (FeedIterator<ViewsByReferrerByDate> feedIterator = this.c_viewEvents.GetItemQueryIterator<ViewsByReferrerByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByReferrerByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        item.id = $"{NormalizeKey(item.referrer)}::{item.day}";
                        await this.c_viewsByReferrerByDate.UpsertItemAsync(item, new PartitionKey(item.dateType));
                    }
                }
            }
        }

        public async Task GetAndSaveViewsByCountryByDate(string dateType)
        {
            if (dateType != "day") return;

            string query = "SELECT @dateType as dateType, c.day, c.country, c.countryName, count(1) as views " +
                "FROM c WHERE c.day > @firstDay AND c.isSpider = false AND IS_DEFINED(c.country) AND c.country != null " +
                "GROUP BY c.day, c.country, c.countryName ORDER BY c.day desc";

            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", dateType)
                .WithParameter("@firstDay", DateTimeOffset.Now.AddDays(-2).ToString("yyyyMMdd"));

            using (FeedIterator<ViewsByCountryByDate> feedIterator = this.c_viewEvents.GetItemQueryIterator<ViewsByCountryByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByCountryByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        item.id = $"{NormalizeKey(item.country)}::{item.day}";
                        await this.c_viewsByCountryByDate.UpsertItemAsync(item, new PartitionKey(item.dateType));
                    }
                }
            }
        }

        public async Task<List<StatsTimeSeriesPoint>> GetViewsTimeSeries(string grain, string startDay, string endDay)
        {
            if (grain != "day")
            {
                throw new ArgumentException("Only 'day' grain is currently supported.");
            }

            var results = new List<StatsTimeSeriesPoint>();
            string query = "SELECT c.id, c.views FROM c WHERE c.dateType = @dateType AND c.id >= @startDay AND c.id <= @endDay ORDER BY c.id";
            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", grain)
                .WithParameter("@startDay", startDay)
                .WithParameter("@endDay", endDay);

            using (FeedIterator<ViewsByDate> feedIterator = this.c_viewsByDate.GetItemQueryIterator<ViewsByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        results.Add(new StatsTimeSeriesPoint
                        {
                            period = item.id,
                            views = ParseInt(item.views)
                        });
                    }
                }
            }

            return results;
        }

        public async Task<List<StatsKeyValue>> GetTopPages(string startDay, string endDay, int limit)
        {
            var query = "SELECT c.page, c.views FROM c WHERE c.dateType = @dateType AND c.day >= @startDay AND c.day <= @endDay";
            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", "day")
                .WithParameter("@startDay", startDay)
                .WithParameter("@endDay", endDay);

            var rollup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (FeedIterator<ViewsByPathByDate> feedIterator = this.c_viewsByPathByDate.GetItemQueryIterator<ViewsByPathByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByPathByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        string page = item.page ?? "(unknown)";
                        rollup.TryGetValue(page, out int current);
                        rollup[page] = current + ParseInt(item.views);
                    }
                }
            }

            return rollup
                .OrderByDescending(kvp => kvp.Value)
                .Take(limit)
                .Select(kvp => new StatsKeyValue { key = kvp.Key, views = kvp.Value })
                .ToList();
        }

        public async Task<List<StatsKeyValue>> GetViewsByReferrer(string startDay, string endDay, int limit)
        {
            var query = "SELECT c.referrer, c.views FROM c WHERE c.dateType = @dateType AND c.day >= @startDay AND c.day <= @endDay";
            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", "day")
                .WithParameter("@startDay", startDay)
                .WithParameter("@endDay", endDay);

            var rollup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (FeedIterator<ViewsByReferrerByDate> feedIterator = this.c_viewsByReferrerByDate.GetItemQueryIterator<ViewsByReferrerByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByReferrerByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        string referrer = string.IsNullOrWhiteSpace(item.referrer) ? "(direct/unknown)" : item.referrer;
                        rollup.TryGetValue(referrer, out int current);
                        rollup[referrer] = current + ParseInt(item.views);
                    }
                }
            }

            return rollup
                .OrderByDescending(kvp => kvp.Value)
                .Take(limit)
                .Select(kvp => new StatsKeyValue { key = kvp.Key, views = kvp.Value })
                .ToList();
        }

        public async Task<List<StatsKeyValue>> GetViewsByCountry(string startDay, string endDay, int limit)
        {
            var query = "SELECT c.countryName, c.country, c.views FROM c WHERE c.dateType = @dateType AND c.day >= @startDay AND c.day <= @endDay";
            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@dateType", "day")
                .WithParameter("@startDay", startDay)
                .WithParameter("@endDay", endDay);

            var rollup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (FeedIterator<ViewsByCountryByDate> feedIterator = this.c_viewsByCountryByDate.GetItemQueryIterator<ViewsByCountryByDate>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<ViewsByCountryByDate> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        string label = string.IsNullOrWhiteSpace(item.countryName) ? (item.country ?? "(unknown)") : item.countryName;
                        rollup.TryGetValue(label, out int current);
                        rollup[label] = current + ParseInt(item.views);
                    }
                }
            }

            return rollup
                .OrderByDescending(kvp => kvp.Value)
                .Take(limit)
                .Select(kvp => new StatsKeyValue { key = kvp.Key, views = kvp.Value })
                .ToList();
        }

        public async Task<StatsSegmentSummary> GetSegments(string startDay, string endDay, int limitPerCategory)
        {
            StatsSegmentSummary summary = new StatsSegmentSummary();
            string query = "SELECT c.newVisitor, c.js_enabled, c.browser, c.device FROM c " +
                "WHERE c.day >= @startDay AND c.day <= @endDay AND c.isSpider = false";
            QueryDefinition q = new QueryDefinition(query)
                .WithParameter("@startDay", startDay)
                .WithParameter("@endDay", endDay);

            var browserCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var deviceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (FeedIterator<RequestRecord> feedIterator = this.c_viewEvents.GetItemQueryIterator<RequestRecord>(q))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<RequestRecord> response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (item.newVisitor) summary.newVisitors++;
                        else summary.returningVisitors++;

                        if (item.js_enabled) summary.jsEnabled++;
                        else summary.noJs++;

                        string browser = string.IsNullOrWhiteSpace(item.browser) ? "(unknown)" : item.browser;
                        string device = string.IsNullOrWhiteSpace(item.device) ? "(unknown)" : item.device;
                        browserCounts.TryGetValue(browser, out int browserCount);
                        deviceCounts.TryGetValue(device, out int deviceCount);
                        browserCounts[browser] = browserCount + 1;
                        deviceCounts[device] = deviceCount + 1;
                    }
                }
            }

            summary.browsers = browserCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(limitPerCategory)
                .Select(kvp => new StatsKeyValue { key = kvp.Key, views = kvp.Value })
                .ToList();

            summary.devices = deviceCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(limitPerCategory)
                .Select(kvp => new StatsKeyValue { key = kvp.Key, views = kvp.Value })
                .ToList();

            return summary;
        }

        public static string NormalizeDay(DateTimeOffset date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        private static int ParseInt(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant().Replace("/", "_");
        }

    }
}
