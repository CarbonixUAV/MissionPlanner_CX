using MissionPlanner.Utilities;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Speaks warning alerts when <see cref="CarbonixWarningEngine"/> fires state changes.
    /// </summary>
    public class SpeechWarningConsumer
    {
        readonly ISpeech _speech;

        public SpeechWarningConsumer(ISpeech speech)
        {
            _speech = speech;
        }

        public void OnWarningStateChanged(object sender, WarningStateChangedEventArgs e)
        {
            if (!e.IsActive)
                return;

            if (_speech == null || !_speech.speechEnable)
                return;

            _speech.SpeakAsync(e.Rule.Text);
        }
    }
}
