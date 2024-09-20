using System.Collections.Generic;
using Newtonsoft.Json;

namespace IncomingWebhooks.VolunteerModel
{
    public class VolunteerDto
    {
        [JsonProperty("Individual")]
        public RegistrantModel Individual { get; set; }

        [JsonProperty("Event")]
        public EventRegistrationModel Event { get; set; }

        [JsonProperty("Sessions")]
        public List<SessionRegistrationModel> Sessions { get; set; }
    }





}
