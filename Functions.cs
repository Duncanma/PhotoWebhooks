using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using System.Web.Http;
using System.Linq;
using Azure.Storage.Queues;
using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using Azure.Storage.Blobs;
using MaxMind.GeoIP2;



namespace PhotoWebhooks
{
    public static class Functions
    {

        const string incomingQueue = "incoming-checkout-complete";
        const string sendEmailQueue = "send-image";
        const string analyticsEvent = "analytics-event";
        static readonly byte[] TrackingGif = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x81, 0x00, 0x00, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x21, 0xff, 0x0b, 0x4e, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2e, 0x30, 0x03, 0x01, 0x01, 0x00, 0x00, 0x21, 0xf9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2c, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x08, 0x04, 0x00, 0x01, 0x04, 0x04, 0x00, 0x3b };

        static Functions()
        {
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

            QueueClient queueClient = new QueueClient(connectionString, incomingQueue);
            queueClient.CreateIfNotExists();
            queueClient = new QueueClient(connectionString, sendEmailQueue);
            queueClient.CreateIfNotExists();
            queueClient = new QueueClient(connectionString, analyticsEvent);
            queueClient.CreateIfNotExists();
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

                if (!RequestIsCrawler(request) && !RequestIsLocal(request))
                {
                    string message = JsonSerializer.Serialize(request);
                    await queueClient.SendMessageAsync(message);
                }

                if (!String.IsNullOrEmpty(request.referrer)) {
                    request.simple_referrer = SimplifyReferrer(request.referrer);
                }

                req.HttpContext.Response.Headers.Add("Cache-Control", "private, max-age=3600");
                var pxImg = new FileContentResult(TrackingGif, "image/gif");
                return pxImg;
            }
            catch (Exception e)
            {
                log.LogError(e, e.Message);
                throw;
            }
        }

        private static string SimplifyReferrer(string referrer)
        {
            Uri uri; 

            if (Uri.TryCreate(referrer, UriKind.Absolute, out uri))
            {
                string host = uri.Host.ToLower();

                if (host.Contains("reddit."))
                {
                    return "Reddit";
                }

                if (referrer.Contains("linkedin."))
                {
                    return "LinkedIn";
                }

                if (referrer.Contains("google."))
                {
                    return "Google";
                }

                return host;
            }

            if (referrer == "android-app://com.slack/")
            {
                return "Slack";
            }

            return null;

        }

        private static bool RequestIsLocal(RequestRecord request)
        {
            string url = request.url ?? string.Empty;

            return url.StartsWith("http://localhost");

        }

        private static string[] crawlerStrings = new string[] { 
            "Bytespider", "AhrefsBot", "bingbot" };

        private static bool RequestIsCrawler(RequestRecord request)
        {
            bool isCrawler = false;
            for (int i = 0; i < crawlerStrings.Length; i++)
            {
                if (request.user_agent.Contains(crawlerStrings[i]))
                {
                    isCrawler = true;
                    break;
                }
            }

            return isCrawler;
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
                    }
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
            return request;
        }
        private static string GetIpFromRequestHeaders(HttpRequest req)
        {
            return (req.Headers["X-Forwarded-For"]
                 .FirstOrDefault() ?? "").Split(new char[] { ':' }).FirstOrDefault();
        }

        [FunctionName("CheckoutComplete")]
        public static async Task<IActionResult> checkoutComplete(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            Console.WriteLine("Checkout Complete");
            //get stripe key
            var stripeKey 
                = Environment.GetEnvironmentVariable("StripeKey");
            var webhookSigningSecret 
                = Environment.GetEnvironmentVariable("WebhookSigningSecret");
            string connectionString 
                = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            QueueClientOptions queueClientOptions = new QueueClientOptions()
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };

            QueueClient queueClient 
                = new QueueClient(connectionString, incomingQueue, queueClientOptions);
            queueClient.CreateIfNotExists();

            log.LogInformation("Stripe Webhook called");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            StripeConfiguration.ApiKey = stripeKey;
            StripeConfiguration.AppInfo 
                = new AppInfo() { Name = "CheckoutComplete Azure Function" };

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                  requestBody,
                  req.Headers["Stripe-Signature"],
                  webhookSigningSecret
                );
                if (stripeEvent.Type == Events.CheckoutSessionCompleted)
                {
                    log.LogInformation(
                        $"Checkout Session Completed Event: {stripeEvent.Id}");

                    var session = stripeEvent.Data.Object as Session;
                    var options = new SessionGetOptions();
                    options.AddExpand("line_items");

                    var service = new SessionService();
                    Session sessionWithLineItems = service.Get(session.Id, options);
                    StripeList<LineItem> lineItems = sessionWithLineItems.LineItems;

                    //get the product id, create an object to pass to the next step 
                    //productId, ReceiptURL, Customer Email
                    //put into a queue
                    if (lineItems != null
                        && lineItems.Count() == 1
                        && session.CustomerDetails != null
                        && session.CustomerDetails.Email != null)
                    {
                        var lineItem = lineItems.First();
                        var productId = lineItem.Price.ProductId;
                        var customerEmail = sessionWithLineItems.CustomerDetails.Email;
                        var customerName = sessionWithLineItems.CustomerDetails.Name;
                        if (customerName == null) { 
                            customerName = customerEmail; 
                        }
                        IncomingOrder order 
                            = new IncomingOrder() { 
                                CustomerEmail = customerEmail, 
                                CustomerName = customerName, 
                                ProductId = productId };
                        string message = JsonSerializer.Serialize(order);
                        await queueClient.SendMessageAsync(message);
                    }
                    else
                    {
                        string message = $"Insufficient details on session {session.Id}";
                        log.LogError(message);
                        throw new Exception(message);
                    }

                }
                else
                {
                    log.LogInformation($"ignored event: {stripeEvent.Type} ");
                    Console.WriteLine("Ignorning this event");
                    Console.WriteLine(stripeEvent.Type);
                }
                return new OkResult();
            }
            catch (StripeException e)
            {
                log.LogError(e.Message);
                Console.WriteLine(e.Message);

                return new BadRequestErrorMessageResult(e.Message);
            }
            //any other exception will throw,
            //which is desired behaviour (returns a 500, etc.)

        }


        [FunctionName("ProcessOrder")]
        public static async Task processOrder(
            [QueueTrigger(
                incomingQueue, 
                Connection = "AZURE_STORAGE_CONNECTION_STRING")
            ] string queueMessage,
            ILogger log)
        {
            IncomingOrder order 
                = JsonSerializer.Deserialize<IncomingOrder>(queueMessage);

            string connectionString 
                = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            QueueClientOptions queueClientOptions = new QueueClientOptions()
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };

            QueueClient queueClient = new QueueClient(
                connectionString, sendEmailQueue, queueClientOptions);
            queueClient.CreateIfNotExists();

            log.LogInformation(
                $"Process Order Called : {order.ProductId} {order.CustomerEmail}");

            //fetch image blob
            string imageFile = $"{order.ProductId}.jpg";

            BlobServiceClient blobServiceClient 
                = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient 
                = blobServiceClient.GetBlobContainerClient("originals");
            BlobClient blobClient 
                = containerClient.GetBlobClient(imageFile);

            //create a unique URL to the image for this customer, with 30-day expiry
            Azure.Storage.Sas.BlobSasBuilder builder 
                = new Azure.Storage.Sas.BlobSasBuilder(
                    Azure.Storage.Sas.BlobSasPermissions.Read, 
                    DateTimeOffset.Now.AddDays(30));

            builder.ContentDisposition = $"attachment; filename={imageFile}";

            var imageURL = blobClient.GenerateSasUri(builder);


            //add to the order, using my custom domain
            order.ImageURL
                = imageURL.ToString().Replace(
                    "https://duncanmackenzieblog.blob.core.windows.net/originals/",
                    "https://originals.duncanmackenzie.net/");

            string message = JsonSerializer.Serialize(order);
            await queueClient.SendMessageAsync(message);
        }


        [FunctionName("SendLink")]
        public static async Task sendLink(
            [QueueTrigger(sendEmailQueue, 
            Connection = "AZURE_STORAGE_CONNECTION_STRING")
            ] string queueMessage,
            ILogger log)
        {
            IncomingOrder order 
                = JsonSerializer.Deserialize<IncomingOrder>(queueMessage);

            log.LogInformation(
                $"Request to send message: {order.ProductId} {order.CustomerEmail}");

            string connectionString 
                = Environment.GetEnvironmentVariable("EmailServiceConnectionString");

            var emailClient = new EmailClient(connectionString);


            string htmlMessage = "<html><h1>Your photo order</h1>" +
                "<p>Thank you for your order from DuncanMackenzie.net.</p>" +
                $"<p>To <a href=\"{order.ImageURL}\">retrieve the full size" + 
                " version of your photo, click this link</a>. " +
                "The file will be large, but should download to your device.</p>" +
                "<p>Once downloaded, you should save this image somewhere safe, " + 
                "as this link will only work for 30 days.</p>" +
                "<p>If you have any questions, please feel free to email me " + 
                "at <a href=\"mailto:support@duncanmackenzie.net\">" + 
                "support@duncanmackenzie.net</a></p>" +
                "</html>";

            string plainMessage = "Your photo order\n\n" +
                "Thank you for your order from DuncanMackenzie.net.\n" +
                "To retrieve the full size version of your photo, " 
                + $"click this link: {order.ImageURL} \n\n" +
                "The file will be large, but should download to your device.\n" +
                "Once downloaded, you should save this image somewhere safe, " +  
                "as this link will only work for 30 days.\n\n" +
                "If you have any questions, please feel free to email"
                + "me at support@duncanmackenzie.net\n\n";

            EmailRecipients recipients = new EmailRecipients();
            recipients.To.Add(
                new EmailAddress(order.CustomerEmail, order.CustomerName));
            recipients.BCC.Add(
                new EmailAddress("support@duncanmackenzie.net", "Support"));

            EmailContent content
                = new EmailContent("Your photo order from DuncanMackenzie.net");
            content.PlainText = plainMessage;
            content.Html = htmlMessage;

            EmailMessage messageToSend
                = new EmailMessage("DoNotReply@messaging.duncanmackenzie.net", 
                recipients, content);

            await emailClient.SendAsync(WaitUntil.Started, messageToSend);

            log.LogInformation(
                $"Message sent: {order.ProductId} {order.CustomerEmail}");
        }
    }
}
