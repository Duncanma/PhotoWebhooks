using Microsoft.WindowsAzure.Storage.Table;
using System;


namespace PhotoWebhooks
{
    internal class RequestRecord
    {
        public string id {  get; set; }
        public string requestTime { get; set; }

        //simplified version of the day portion (20240503)
        public string day { get; set; }
        //the permalink/canonical relative form of my URLs (like /about or /blog)
        public string page { get; set; }

        //the full URL that was requested, with whatever query strings might be on it
        public string url { get; set; }

        public string ip_address { get; set; }

        public string title { get; set; }
        public string referrer { get; set; }
        public string simple_referrer { get; set; }
        public string accept_lang { get; set; }
        public string user_agent { get; set; }
        public string country { get; set; }
        public bool js_enabled { get; set; }
    }
}
