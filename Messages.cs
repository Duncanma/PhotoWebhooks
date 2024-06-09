using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoWebhooks
{
    public class IncomingOrder
    {
        public string ProductId {  get; set; }
        public string CustomerEmail { get; set; }
        public string CustomerName { get; set; }
        public string ImageURL { get; set; }
        public string SessionID { get; set; }
        public string EventID { get; set; }
    }
}
