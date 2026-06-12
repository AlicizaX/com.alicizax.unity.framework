
namespace AlicizaX.Localization
{
    [Prewarm(256)]
    public readonly struct LocalizationChangeEvent : IPayloadEventArgs
    {
        public readonly string ChangedLanguage;

        public LocalizationChangeEvent(string language)
        {
            ChangedLanguage = language;
        }

        public static void Publisher(string language)
        {
            EventBus.Publish(new LocalizationChangeEvent(language));
        }
    }
}
