using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PokeBattle
{
    /// <summary>
    /// Talks to Groq's OpenAI-compatible chat completions endpoint to generate
    /// short in-character lines for creatures. All battle logic lives elsewhere;
    /// this class only knows how to turn a prompt into a one-liner.
    ///
    /// NOTE: storing the API key in a SerializeField is fine for a local
    /// prototype / workshop. Do NOT ship a build with a real key embedded.
    /// </summary>
    public class GroqClient : MonoBehaviour
    {
        private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
        private const string TtsEndpoint = "https://api.groq.com/openai/v1/audio/speech";

        [Header("Groq API")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string model = "llama-3.3-70b-versatile";

        [Header("Generation")]
        [SerializeField, Range(8, 120)] private int maxTokens = 60;
        [SerializeField, Range(0f, 2f)] private float temperature = 0.9f;
        [Tooltip("Abort the request after this many seconds and use a canned line instead.")]
        [SerializeField] private float timeoutSeconds = 8f;

        [Header("Text-to-Speech (Groq PlayAI)")]
        [Tooltip("If off (or no API key), lines are shown silently with no audio.")]
        [SerializeField] private bool enableTts = true;
        [SerializeField] private string ttsModel = "playai-tts";
        [Tooltip("PlayAI voice, e.g. Fritz-PlayAI, Celeste-PlayAI, Thunder-PlayAI, Atlas-PlayAI.")]
        [SerializeField] private string ttsVoice = "Fritz-PlayAI";
        [Tooltip("Optional: assign an AudioSource. One is added automatically if left empty.")]
        [SerializeField] private AudioSource audioSource;

        public bool HasApiKey => !string.IsNullOrWhiteSpace(apiKey);
        public bool TtsEnabled => enableTts && HasApiKey;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }
            audioSource.playOnAwake = false;
        }

        /// <summary>
        /// Generates a short spoken line for a creature and delivers it via callback.
        /// Never throws and never blocks: on any failure it returns a canned line.
        /// </summary>
        public void Speak(string speakerName, ElementType type, string opponentName, string situation, string lastOpponentLine, Action<string> onResult)
        {
            StartCoroutine(SpeakRoutine(speakerName, type, opponentName, situation, lastOpponentLine, onResult));
        }

        private IEnumerator SpeakRoutine(string speakerName, ElementType type, string opponentName, string situation, string lastOpponentLine, Action<string> onResult)
        {
            if (!HasApiKey)
            {
                onResult?.Invoke(Fallback(speakerName, situation));
                yield break;
            }

            string systemPrompt =
                "Ikaw ang boses ng isang nilalang sa isang Pokemon-style na laban. " +
                "Mang-aaway ka at mang-iinsulto sa kalaban para masaktan ang damdamin nito (trash talk). " +
                "Sumagot ka ng ISANG maikling linya LANG sa Filipino o Taglish (hanggang 12 salita). " +
                "Maging savage, mayabang, at nakakatusok pero PLAYFUL — walang mura, slur, o tunay na pang-aapi. " +
                "Banggitin ang pangalan ng kalaban kung kaya. Walang quotes, walang emoji, walang paliwanag.";

            string opponentContext = string.IsNullOrWhiteSpace(lastOpponentLine)
                ? "Wala pang sinabi ang kalaban."
                : $"Sinabi ng kalaban: \"{lastOpponentLine}\". Sagutin/gantihan mo siya.";

            string userPrompt =
                $"Ikaw si {speakerName} (uri: {type}). Kalaban mo si {opponentName}. " +
                $"Sitwasyon: {situation}. {opponentContext} Sabihin ang iyong panunukso.";

            var request = new ChatRequest
            {
                model = model,
                max_tokens = maxTokens,
                temperature = temperature,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = systemPrompt },
                    new ChatMessage { role = "user", content = userPrompt }
                }
            };

            string json = JsonUtility.ToJson(request);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest(Endpoint, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(body);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.timeout = Mathf.CeilToInt(timeoutSeconds);
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + apiKey);

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[GroqClient] Request failed ({www.responseCode}): {www.error}. Using fallback line.");
                    onResult?.Invoke(Fallback(speakerName, situation));
                    yield break;
                }

                string line = ParseLine(www.downloadHandler.text);
                onResult?.Invoke(string.IsNullOrWhiteSpace(line) ? Fallback(speakerName, situation) : line);
            }
        }

        /// <summary>
        /// Synthesizes the given text with Groq's PlayAI TTS endpoint and plays it.
        /// Calls onComplete when playback finishes (or immediately if TTS is off/fails),
        /// so callers can keep the speech bubble up while the line is spoken.
        /// </summary>
        public void PlaySpeech(string text, Action onComplete)
        {
            StartCoroutine(PlaySpeechRoutine(text, onComplete));
        }

        private IEnumerator PlaySpeechRoutine(string text, Action onComplete)
        {
            if (!TtsEnabled || string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke();
                yield break;
            }

            var request = new TtsRequest
            {
                model = ttsModel,
                input = text,
                voice = ttsVoice,
                response_format = "wav"
            };

            string json = JsonUtility.ToJson(request);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest(TtsEndpoint, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(body);
                var audioHandler = new DownloadHandlerAudioClip(TtsEndpoint, AudioType.WAV);
                audioHandler.streamAudio = false;
                www.downloadHandler = audioHandler;
                www.timeout = Mathf.CeilToInt(timeoutSeconds);
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + apiKey);

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[GroqClient] TTS failed ({www.responseCode}): {www.error}.");
                    onComplete?.Invoke();
                    yield break;
                }

                AudioClip clip = null;
                try { clip = audioHandler.audioClip; }
                catch (Exception e) { Debug.LogWarning("[GroqClient] Could not decode TTS audio: " + e.Message); }

                if (clip == null)
                {
                    onComplete?.Invoke();
                    yield break;
                }

                audioSource.Stop();
                audioSource.clip = clip;
                audioSource.Play();

                while (audioSource.isPlaying)
                    yield return null;

                onComplete?.Invoke();
            }
        }

        private static string ParseLine(string responseJson)
        {
            try
            {
                ChatResponse parsed = JsonUtility.FromJson<ChatResponse>(responseJson);
                if (parsed != null && parsed.choices != null && parsed.choices.Length > 0)
                {
                    string content = parsed.choices[0].message.content;
                    return CleanUp(content);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GroqClient] Could not parse response: " + e.Message);
            }
            return null;
        }

        private static string CleanUp(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw.Trim();
            // Strip surrounding quotes the model sometimes adds.
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && (s[s.Length - 1] == '"' || s[s.Length - 1] == '\''))
                s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        private static string Fallback(string creatureName, string situation)
        {
            string[] lines =
            {
                "Ang hina mo naman, nakakahiya!",
                "Umuwi ka na, talo ka na dito!",
                "Yan lang? Natutulog pa ako, oy!",
                "Iyak ka na, wala kang kalaban-laban!",
                "Mukha mo, takbo na habang maaga!",
                "Sayang ang oras ko sa pitsugin na parang ikaw!",
                "Ang bagal mo, parang kuhol ka lang!",
                "Tapos ka na bago pa magsimula, lampa!"
            };
            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }

        // --- JSON DTOs (JsonUtility-friendly: plain fields, arrays allowed) ---

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ChatRequest
        {
            public string model;
            public ChatMessage[] messages;
            public int max_tokens;
            public float temperature;
        }

        [Serializable]
        private class ChatResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice
        {
            public ChatMessage message;
        }

        [Serializable]
        private class TtsRequest
        {
            public string model;
            public string input;
            public string voice;
            public string response_format;
        }
    }
}
