using Newtonsoft.Json;

namespace IncomingWebhooks.VolunteerModel
{
    public class SessionRegistrationModel
    {        
        public string Id { get; set; }

        [JsonProperty("Title")]
        public string Title { get; set; }

        [JsonProperty("Status")]
        public string RegistrationStatus { get; set; }

        public bool IsRegistered { get; set; } 
    }





}
