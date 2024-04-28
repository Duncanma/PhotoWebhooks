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
using Microsoft.WindowsAzure.Storage.Blob;
using Azure.Storage.Blobs;
using Azure.Storage.Queues.Models;


namespace PhotoWebhooks
{
    public static class Functions
    {

        const string incomingQueue = "incoming-checkout-complete";
        const string sendEmailQueue = "send-image";

        static Functions()
        {
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

            QueueClient queueClient = new QueueClient(connectionString, incomingQueue);
            queueClient.CreateIfNotExists();
            queueClient = new QueueClient(connectionString, sendEmailQueue);
            queueClient.CreateIfNotExists();

        }

        [FunctionName("CheckoutComplete")]
        public static async Task<IActionResult> checkoutComplete(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            Console.WriteLine("Checkout Complete");
            //get stripe key
            var stripeKey = Environment.GetEnvironmentVariable("StripeKey");
            var webhookSigningSecret = Environment.GetEnvironmentVariable("WebhookSigningSecret");
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            QueueClientOptions queueClientOptions = new QueueClientOptions()
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };

            QueueClient queueClient = new QueueClient(connectionString, incomingQueue, queueClientOptions);
            queueClient.CreateIfNotExists();

            log.LogInformation("Stripe Webhook called");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            StripeConfiguration.ApiKey = stripeKey;

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                  requestBody,
                  req.Headers["Stripe-Signature"],
                  webhookSigningSecret
                );
                if (stripeEvent.Type == Events.CheckoutSessionCompleted)
                {
                    log.LogInformation($"Checkout Session Completed Event: {stripeEvent.Id}");

                    var session = stripeEvent.Data.Object as Session;
                    var options = new SessionGetOptions();
                    options.AddExpand("line_items");

                    var service = new SessionService();
                    // Retrieve the session. If you require line items in the response, you may include them by expanding line_items.
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
                        IncomingOrder order = new IncomingOrder() { CustomerEmail = customerEmail, CustomerName = customerName, ProductId = productId };
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
            //any other exception will throw, which is desired behaviour (returns a 500, etc.)

        }

        //ProcessImage
        //SendImage

        [FunctionName("ProcessOrder")]
        public static async Task processOrder(
            [QueueTrigger(incomingQueue, Connection = "AZURE_STORAGE_CONNECTION_STRING")] string queueMessage,
            ILogger log)
        {
            Console.WriteLine("Process Order");

            Console.WriteLine(queueMessage);

            IncomingOrder order = JsonSerializer.Deserialize<IncomingOrder>(queueMessage);

            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            QueueClientOptions queueClientOptions = new QueueClientOptions()
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };

            QueueClient queueClient = new QueueClient(connectionString, sendEmailQueue, queueClientOptions);
            queueClient.CreateIfNotExists();

            log.LogInformation($"Process Order Called : {order.ProductId} {order.CustomerEmail}");

            //fetch image blob
            string imageFile = $"{order.ProductId}.jpg";

            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("originals");

            BlobClient blobClient = containerClient.GetBlobClient(imageFile);

            //create a unique URL to the image for this customer, with 30-day expiry
            Azure.Storage.Sas.BlobSasBuilder builder = new Azure.Storage.Sas.BlobSasBuilder(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.Now.AddDays(30));

            builder.ContentDisposition = $"attachment; filename={imageFile}";

            var imageURL = blobClient.GenerateSasUri(builder);


            //add to the order
            order.ImageURL = imageURL.ToString().Replace("https://duncanmackenzieblog.blob.core.windows.net/originals/", "https://originals.duncanmackenzie.net/");

            string message = JsonSerializer.Serialize(order);
            Console.WriteLine(message);
            await queueClient.SendMessageAsync(message);
        }


        [FunctionName("SendLink")]
        public static async Task sendLink(
    [QueueTrigger(sendEmailQueue, Connection = "AZURE_STORAGE_CONNECTION_STRING")] string queueMessage,
    ILogger log)
        {
            Console.WriteLine("Send Link");
            Console.WriteLine(queueMessage);

            IncomingOrder order = JsonSerializer.Deserialize<IncomingOrder>(queueMessage);

            log.LogInformation($"Request to send message: {order.ProductId} {order.CustomerEmail}");
            // This code retrieves your connection string from an environment variable.
            string connectionString = Environment.GetEnvironmentVariable("EmailServiceConnectionString");
            var emailClient = new EmailClient(connectionString);


            string htmlMessage = "<html><h1>Your photo order</h1>" +
                "<p>Thank you for your order from DuncanMackenzie.net.</p>" +
                $"<p>To <a href=\"{order.ImageURL}\">retrieve the full size version of your photo, click this link</a>. " +
                "The file will be large, but should download to your device.</p>" +
                "<p>Once downloaded, you should save this image somewhere safe, as this link will only work for 30 days.</p>" +
                "<p>If you have any questions, please feel free to email me at <a href=\"mailto:support@duncanmackenzie.net\">support@duncanmackenzie.net</a></p>" +
                "</html>";

            string plainMessage = "Your photo order\n\n" +
                "Thank you for your order from DuncanMackenzie.net.\n" +
                $"To retrieve the full size version of your photo, click this link: {order.ImageURL} \n\n" +
                "The file will be large, but should download to your device.\n" +
                "Once downloaded, you should save this image somewhere safe, as this link will only work for 30 days.\n\n" +
                "If you have any questions, please feel free to email me at support@duncanmackenzie.net\n\n";

            EmailRecipients recipients = new EmailRecipients();
            recipients.To.Add(new EmailAddress(order.CustomerEmail, order.CustomerName));
            recipients.BCC.Add(new EmailAddress("support@duncanmackenzie.net", "Support"));

            EmailContent content = new EmailContent("Your photo order from DuncanMackenzie.net");
            content.PlainText = plainMessage;
            content.Html = htmlMessage;

            EmailMessage messageToSend = new EmailMessage("DoNotReply@messaging.duncanmackenzie.net", recipients, content);

            await emailClient.SendAsync(WaitUntil.Started, messageToSend);

            log.LogInformation($"Message sent: {order.ProductId} {order.CustomerEmail}");
        }
    }
}
