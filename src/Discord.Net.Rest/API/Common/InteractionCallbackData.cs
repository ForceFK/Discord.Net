using Discord.API.Rest;
using Newtonsoft.Json;
using System.Linq;

namespace Discord.API
{
    internal class InteractionCallbackData
    {
        [JsonProperty("tts")]
        public Optional<bool> TTS { get; set; }

        [JsonProperty("content")]
        public Optional<string> Content { get; set; }

        [JsonProperty("embeds")]
        public Optional<Embed[]> Embeds { get; set; }

        [JsonProperty("allowed_mentions")]
        public Optional<AllowedMentions> AllowedMentions { get; set; }

        [JsonProperty("flags")]
        public Optional<MessageFlags> Flags
        {
            get
            {
                var flags = _flags.GetValueOrDefault(MessageFlags.None);
                if (Components.IsSpecified && Components.Value?.Any(x => x.Type is not ComponentType.ActionRow) == true)
                    flags |= MessageFlags.ComponentsV2;

                return flags == MessageFlags.None && !_flags.IsSpecified
                    ? Optional<MessageFlags>.Unspecified
                    : flags;
            }
            set => _flags = value;
        }

        private Optional<MessageFlags> _flags;

        [JsonProperty("components")]
        public Optional<IMessageComponent[]> Components { get; set; }

        [JsonProperty("choices")]
        public Optional<ApplicationCommandOptionChoice[]> Choices { get; set; }

        [JsonProperty("title")]
        public Optional<string> Title { get; set; }

        [JsonProperty("custom_id")]
        public Optional<string> CustomId { get; set; }

        [JsonProperty("poll")]
        public Optional<CreatePollParams> Poll { get; set; }
    }
}
