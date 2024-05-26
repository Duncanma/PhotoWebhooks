using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;


namespace PhotoWebhooks
{

    public static class CacheResponseExtensions
    {
        public static HttpResponseMessage CreateCachedResponse<T>(this HttpRequestMessage request, HttpStatusCode statusCode, T value, TimeSpan? maxAge = null)
        {
            HttpResponseMessage responseMessage = request.CreateResponse<T>(statusCode, value);
            responseMessage.Headers.CacheControl = new CacheControlHeaderValue()
            {
                Public = true,
                MaxAge = maxAge
            };
            return responseMessage;
        }
    }
}