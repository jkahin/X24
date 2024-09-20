using netForum.Integration.Webhooks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;


namespace IncomingWebhooks
{
    public class Volunteer : WebhookReceiverBase
    {
        public override HttpResponseMessage ProcessRequest(WebhookIntegration integrationSettings, 
            HttpRequestMessage requestMessage)
        {            
            var handler = new VolunteerHandler();
            return handler.ProcessVolunteer(integrationSettings, requestMessage);
        }
    }
}
