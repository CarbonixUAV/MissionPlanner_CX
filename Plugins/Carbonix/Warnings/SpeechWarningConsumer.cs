using Carbonix.CAS;
using MissionPlanner.Utilities;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Announces new crew alerts via text-to-speech.
    /// </summary>
    public class SpeechWarningConsumer
    {
        readonly ISpeech _speech;

        public SpeechWarningConsumer(ISpeech speech)
        {
            _speech = speech;
        }

        public void OnNewAlert(AlertEntry entry)
        {
            if (_speech == null || !_speech.speechEnable)
                return;

            _speech.SpeakAsync(entry.Message);
        }
    }
}
