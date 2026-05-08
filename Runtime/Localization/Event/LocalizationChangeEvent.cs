
namespace AlicizaX.Localization
{
    [Prewarm(4)]
    public readonly struct LocalizationChangeEvent : IEventArgs
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
