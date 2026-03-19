using System;
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
        readonly Func<bool> _isArmed;

        public SpeechWarningConsumer(ISpeech speech, Func<bool> isArmed)
        {
            _speech = speech;
            _isArmed = isArmed;
        }

        public void OnNewAlert(AlertEntry entry)
        {
            if (_speech == null || !_speech.speechEnable)
                return;

            if (!_isArmed())
                return;

            if (entry.Severity == WarningSeverity.Advisory)
                return;

            _speech.SpeakAsync(entry.Message);
        }
    }
}
