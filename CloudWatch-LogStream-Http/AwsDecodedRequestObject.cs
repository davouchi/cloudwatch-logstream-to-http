using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace CloudWatch_LogStream_Http
{
  
    public class LogEventsItem
    {
        
        public string id { get; set; }
       
        public long timestamp { get; set; }
     
        public string message { get; set; }       
    }

    public class AwsDecodedRequestObject
    {
       
        public string messageType { get; set; }
    
        public string owner { get; set; }
       
        public string logGroup { get; set; }
        
        public string logStream { get; set; }
       
        public List<string> subscriptionFilters { get; set; }

        public List<LogEventsItem> logEvents { get; set; }
       
    }

   
}
