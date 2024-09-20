using Newtonsoft.Json;

namespace IncomingWebhooks.VolunteerModel
{
    public class RegistrantModel
    {
        public string Id { get; set; }

        public string CustomerAddressId { get; set; }

        [JsonProperty("FirstName")]
        public string FirstName { get; set; }

        [JsonProperty("LastName")]
        public string LastName { get; set; }

        [JsonProperty("EmailAddress")]
        public string EmailAddress { get; set; }
    }





}
