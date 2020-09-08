using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SpeechTranslatorBlazor.Server.Hubs;
using SpeechTranslatorBlazor.Shared;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechTranslatorBlazor.Server
{
    public class TranslationEngine : IDisposable
    {

        const int SAMPLES_PER_SECOND = 16000;
        const int BITS_PER_SAMPLE = 16;
        const int NUMBER_OF_CHANNELS = 1;
        const int BUFFER_SIZE = 320 * 2;
        ConcurrentQueue<byte[]> _audioToWrite = new ConcurrentQueue<byte[]>();
        private readonly IConfiguration _config;
        private readonly IHubContext<TranslationHub> _hub;
        private string _uuid;
        private string _languageSpoken;
        private string _languageTranslated;
        

        SpeechTranslationConfig _translationConfig;
        SpeechConfig _speechConfig;
        PushAudioInputStream _inputStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(SAMPLES_PER_SECOND, BITS_PER_SAMPLE, NUMBER_OF_CHANNELS));
        AudioConfig _audioInput;
        TranslationRecognizer _recognizer;
        SpeechSynthesizer _synthesizer;
        AudioOutputStream _audioOutputStream;
        AudioConfig _output;

        public TranslationEngine(IConfiguration config, IHubContext<TranslationHub> hub)
        {
            _hub = hub;
            _config = config;
            _translationConfig = SpeechTranslationConfig.FromSubscription(_config["SUBSCRIPTION_KEY"], _config["REGION"]);
            _speechConfig = SpeechTranslationConfig.FromSubscription(_config["SUBSCRIPTION_KEY"], _config["REGION"]);
            _audioInput = AudioConfig.FromStreamInput(_inputStream);
            _audioOutputStream = AudioOutputStream.CreatePullStream();
            _output = AudioConfig.FromStreamOutput(_audioOutputStream);            
        }

        private void RecognizerRecognized(object sender, TranslationRecognitionEventArgs e)
        {
            var translationLanguage = _languageTranslated.Split("-")[0];
            var translation = e.Result.Translations[translationLanguage].ToString();
            Trace.WriteLine("Recognized: " + translation);
            var ttsAudio = _synthesizer.SpeakTextAsync(translation).Result.AudioData;
            var translationResult = new Translation
            {
                LanguageSpoken = _languageSpoken,
                LanguageTranslated = _languageTranslated,
                Text = translation,
                UUID = _uuid
            };
            _hub.Clients.All.SendAsync("receiveTranslation", translationResult);
            _audioToWrite.Enqueue(ttsAudio);
        }

        private async Task StartSpeechTranscriptionEngine(string recognitionLanguage, string targetLanguage)
        {
            _translationConfig.SpeechRecognitionLanguage = recognitionLanguage;
            _translationConfig.AddTargetLanguage(targetLanguage);
            _speechConfig.SpeechRecognitionLanguage = targetLanguage;
            _speechConfig.SpeechSynthesisLanguage = targetLanguage;
            _synthesizer = new SpeechSynthesizer(_speechConfig, _output);
            _recognizer = new TranslationRecognizer(_translationConfig, _audioInput);            
            _recognizer.Recognized += RecognizerRecognized;
            await _recognizer.StartContinuousRecognitionAsync();
        }

        private async Task StopTranscriptionEngine()
        {
            if (_recognizer != null)
            {
                _recognizer.Recognized -= RecognizerRecognized;
                await _recognizer.StopContinuousRecognitionAsync();
            }
        }

        public void Dispose()
        {
            _inputStream.Dispose();
            _audioInput.Dispose();
            _recognizer.Dispose();
        }

        public async Task ReceiveAudioOnWebSocket(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[BUFFER_SIZE];

            try
            {                
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var config = JsonConvert.DeserializeObject<Translation>(System.Text.Encoding.Default.GetString(buffer));
                _uuid = config.UUID;
                await StartSpeechTranscriptionEngine(config.LanguageSpoken, 
                    config.LanguageTranslated);
                _languageSpoken = config.LanguageSpoken;
                _languageTranslated = config.LanguageTranslated;
                while (!result.CloseStatus.HasValue)
                {

                    byte[] audio;
                    while (_audioToWrite.TryDequeue(out audio))
                    {
                        const int bufferSize = 640;
                        for (var i = 0; i + bufferSize < audio.Length; i += bufferSize)
                        {
                            var audioToSend = audio[i..(i + bufferSize)];
                            var endOfMessage = audio.Length > (bufferSize + i);
                            await webSocket.SendAsync(new ArraySegment<byte>(audioToSend, 0, bufferSize), WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);
                        }
                    }

                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    _inputStream.Write(buffer);
                }
                await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.ToString());
            }
            finally
            {
                await StopTranscriptionEngine();
            }
        }
    }
}
