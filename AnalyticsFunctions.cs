using Azure.Communication.Email;
using Azure.Storage.Queues;
using Azure;
using MaxMind.GeoIP2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace PhotoWebhooks
{
    public static class AnalyticsFunctions
    {

        const string analyticsEvent = "analytics-event";
        static readonly byte[] TrackingGif = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x81, 0x00, 0x00, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x21, 0xff, 0x0b, 0x4e, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2e, 0x30, 0x03, 0x01, 0x01, 0x00, 0x00, 0x21, 0xf9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2c, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x08, 0x04, 0x00, 0x01, 0x04, 0x04, 0x00, 0x3b };

        static UAParser.Parser parser;

        static AnalyticsFunctions()
        {
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

            QueueClient queueClient = new QueueClient(connectionString, analyticsEvent);
            queueClient.CreateIfNotExists();
            parser = UAParser.Parser.GetDefault();

        }

        [FunctionName("event")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
        public static async Task<IActionResult> trackAnalyticsEvent(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {
                string connectionString
                    = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

                QueueClientOptions queueClientOptions = new QueueClientOptions()
                {
                    MessageEncoding = QueueMessageEncoding.Base64
                };

                QueueClient queueClient
                    = new QueueClient(connectionString, analyticsEvent, queueClientOptions);

                //queueClient.CreateIfNotExists();

                RequestRecord request = CreateRequestRecordFromRequest(req, log);

                if (!FunctionsHelpers.RequestIsCrawler(request) && !FunctionsHelpers.RequestIsLocal(request))
                {
                    string message = JsonSerializer.Serialize(request);
                    await queueClient.SendMessageAsync(message);
                }

                req.HttpContext.Response.Headers.Add("Cache-Control", "private, max-age=300");
                var pxImg = new FileContentResult(TrackingGif, "image/gif");
                return pxImg;
            }
            catch (Exception e)
            {
                log.LogError(e, e.Message);
                throw;
            }
        }

        [FunctionName("ProcessEvent")]
        public static async Task processEvent(
            [QueueTrigger(
                analyticsEvent,
                Connection = "AZURE_STORAGE_CONNECTION_STRING")
            ] string queueMessage,
            ILogger log)
        {
            RequestRecord request
                = JsonSerializer.Deserialize<RequestRecord>(queueMessage);

            if (request != null)
            {
                if (request.country == null)
                {
                    var maxMindAccountID = Environment.GetEnvironmentVariable("MaxMindAccountID");
                    var maxMindLicenseKey = Environment.GetEnvironmentVariable("MaxMindLicenseKey");
                    int accountID = int.Parse(maxMindAccountID);
                    using (var client = new WebServiceClient(accountID, maxMindLicenseKey, host: "geolite.info"))
                    {
                        var country = await client.CountryAsync(request.ip_address);
                        request.country = country.Country.IsoCode;
                        request.countryName = country.Country.Name;
                    }
                }

                if (!String.IsNullOrEmpty(request.user_agent))
                {
                    var info = parser.Parse(request.user_agent);
                    if (info != null)
                    {
                        request.platform = info.OS.Family;
                        request.device = info.Device.Family;
                        request.browser = info.UA.Family;
                        request.isSpider = info.Device.IsSpider;
                    }

                }

                if (!String.IsNullOrEmpty(request.referrer))
                {
                    request.simple_referrer = FunctionsHelpers.SimplifyReferrer(request.referrer);
                }

                string cosmosEndpoint
                    = Environment.GetEnvironmentVariable("CosmosEndpointUri");
                string cosmosKey
                    = Environment.GetEnvironmentVariable("CosmosPrimaryKey");

                DataStorage data = new DataStorage(cosmosEndpoint, cosmosKey);

                await data.Init();
                await data.CreateRequestItem(request);
            }
        }

        //0 */5 * * * *
        //0 30 0 * * *
        [FunctionName("ComputeViewsByDay")]
        [FixedDelayRetry(5, "00:00:10")]
        public static async Task ComputeViewsByDay([TimerTrigger("0 30 0 * * *")] TimerInfo timerInfo,
            ILogger log)
        {
            Console.WriteLine("ComputeViewsByDay");

            string cosmosEndpoint
                = Environment.GetEnvironmentVariable("CosmosEndpointUri");
            string cosmosKey
                = Environment.GetEnvironmentVariable("CosmosPrimaryKey");

            DataStorage data = new DataStorage(cosmosEndpoint, cosmosKey);

            await data.Init();
            await data.GetAndSaveViewsByDate("day");
        }

        //0 */5 * * * *
        [FunctionName("ComputeViewsByPathByDay")]
        [FixedDelayRetry(5, "00:00:10")]
        public static async Task ComputeViewsByPathByDay([TimerTrigger("0 0 2 * * *")] TimerInfo timerInfo,
            ILogger log)
        {
            Console.WriteLine("ComputeViewsByPathByDay");

            string cosmosEndpoint
                = Environment.GetEnvironmentVariable("CosmosEndpointUri");
            string cosmosKey
                = Environment.GetEnvironmentVariable("CosmosPrimaryKey");

            DataStorage data = new DataStorage(cosmosEndpoint, cosmosKey);

            await data.Init();
            await data.GetAndSaveViewsByPathByDate("day");
        }

        [FunctionName("SendSomeCharts")]
        [FixedDelayRetry(5, "00:00:10")]
        public static async Task SendSomeCharts([TimerTrigger("0 45 2 * * *")] TimerInfo timerInfo,
            ILogger log)
        {
            Console.WriteLine("SendSomeCharts");

            string cosmosEndpoint
                = Environment.GetEnvironmentVariable("CosmosEndpointUri");
            string cosmosKey
                = Environment.GetEnvironmentVariable("CosmosPrimaryKey");

            DataStorage data = new DataStorage(cosmosEndpoint, cosmosKey);

            await data.Init();
            var results = await data.GetViewsByDay(-7);

            string connectionString
                = Environment.GetEnvironmentVariable("EmailServiceConnectionString");

            var emailClient = new EmailClient(connectionString);

            var currentDateTime = DateTimeOffset.Now.ToString("f");

            StringBuilder htmlResults = new StringBuilder();
            StringBuilder plainResults = new StringBuilder();

            string chartURL = Charting.Make7DayLineChart(results);

            htmlResults.Append("<table><thead><tr><th scope='col'>Date</th><th scope='col'>Views</th></tr><tbody>");
            plainResults.Append("Date, Views\n");

            foreach (var row in results)
            {
                htmlResults.Append($"<tr><td>{row.Key}</td><td>{row.Value}</td></tr>");
                plainResults.Append($" - {row.Key}, {row.Value}\n");
            }

            htmlResults.Append("</tbody></table>");
            plainResults.Append("\n\n");

            string htmlMessage = "<html><body><h1>Site Stats</h1>" +
                $"<p>Stats as of {currentDateTime}</p>" +
                $"<p><img style='background-color:white' src='{chartURL}' width=800 height=300></p>" 
                + htmlResults.ToString() + "</body></html>";

            string plainMessage = "Site Stats\n\n" +
                $"Stats as of {currentDateTime}\n\n" + plainResults.ToString() + "\n\n"
                + $"Chart available at: {chartURL}";

            EmailRecipients recipients = new EmailRecipients();

            recipients.To.Add(
            new EmailAddress(Environment.GetEnvironmentVariable("SendAnalyticsReportsTo"), Environment.GetEnvironmentVariable("SendAnalyticsReportsName")));

            EmailContent content
                = new EmailContent("Site Stats");
            content.PlainText = plainMessage;
            content.Html = htmlMessage;

            EmailMessage messageToSend
                = new EmailMessage("DoNotReply@messaging.duncanmackenzie.net",
                recipients, content);

            await emailClient.SendAsync(WaitUntil.Started, messageToSend);
        }

        private static RequestRecord CreateRequestRecordFromRequest(HttpRequest req, ILogger log)
        {
            RequestRecord request = new RequestRecord();
            request.ip_address = GetIpFromRequestHeaders(req);
            request.accept_lang = req.Headers["Accept-Language"];
            request.url = req.Headers["Referer"];
            request.user_agent = req.Headers["User-Agent"];

            var query = req.Query;
            if (query.ContainsKey("page"))
            {
                request.page = query["page"];
            }
            if (query.ContainsKey("title"))
            {
                request.title = query["title"];
            }

            if (query.ContainsKey("referrer"))
            {
                request.referrer = query["referrer"];
            }

            if (query.ContainsKey("js_enabled"))
            {
                request.js_enabled = true;
            }

            DateTimeOffset requestTime = DateTimeOffset.UtcNow;
            Guid id = Guid.NewGuid();
            request.id = $"{requestTime:HH':'mm':'ss'.'fffffff}::{id}";

            request.requestTime = requestTime.ToString("o");
            request.day = requestTime.ToString("yyyyMMdd");

            //session and visitor cookies
            string visit;
            string visitor;

            if (req.Cookies.ContainsKey("visit"))
            {
                visit = req.Cookies["visit"];

            }
            else
            {
                request.startVisit = true;
                visit = Guid.NewGuid().ToString();
            }
            var options = new CookieOptions();
            options.Expires = DateTimeOffset.UtcNow.AddHours(4);
            options.Secure = true;
            options.SameSite = SameSiteMode.Strict;
            req.HttpContext.Response.Cookies.Append("visit", visit, options);

            if (req.Cookies.ContainsKey("visitor"))
            {
                visitor = req.Cookies["visitor"];
            }
            else
            {
                request.newVisitor = true;
                visitor = Guid.NewGuid().ToString();
                options.Expires = DateTimeOffset.UtcNow.AddYears(4);
                req.HttpContext.Response.Cookies.Append("visitor", visitor, options);
            }

            request.visit = visit;
            request.visitor = visitor;

            return request;
        }
        private static string GetIpFromRequestHeaders(HttpRequest req)
        {
            return (req.Headers["X-Forwarded-For"]
                 .FirstOrDefault() ?? "").Split(new char[] { ':' }).FirstOrDefault();
        }

    }
}
