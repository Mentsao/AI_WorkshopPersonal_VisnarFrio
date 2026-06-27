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

        [Header("Groq API")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string model = "llama-3.3-70b-versatile";

        [Header("Generation")]
        [SerializeField, Range(8, 120)] private int maxTokens = 60;
        [SerializeField, Range(0f, 2f)] private float temperature = 0.9f;
        [Tooltip("Abort the request after this many seconds and use a canned line instead.")]
        [SerializeField] private float timeoutSeconds = 8f;

        public bool HasApiKey => !string.IsNullOrWhiteSpace(apiKey);

        /// <summary>
        /// Generates a short spoken line for a creature and delivers it via callback.
        /// Never throws and never blocks: on any failure it returns a canned line.
        /// </summary>
        public void Speak(string creatureName, ElementType type, string situation, Action<string> onResult)
        {
            StartCoroutine(SpeakRoutine(creatureName, type, situation, onResult));
        }

        private IEnumerator SpeakRoutine(string creatureName, ElementType type, string situation, Action<string> onResult)
        {
            if (!HasApiKey)
            {
                onResult?.Invoke(Fallback(creatureName, situation));
                yield break;
            }

            string systemPrompt =
                "You voice a creature in a Pokemon-style battle. " +
                "Reply with ONE short in-character line (max 12 words). " +
                "No quotes, no emojis, no narration. Be punchy and fun.";

            string userPrompt =
                $"Creature: {creatureName} (type: {type}). Situation: {situation}. Say your line.";

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
                    onResult?.Invoke(Fallback(creatureName, situation));
                    yield break;
                }

                string line = ParseLine(www.downloadHandler.text);
                onResult?.Invoke(string.IsNullOrWhiteSpace(line) ? Fallback(creatureName, situation) : line);
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
                "Let's do this!",
                "You can't beat me!",
                "Is that all you've got?",
                "I'm just getting warmed up!",
                "Feel my power!",
                "Nice try!",
                "Too slow!",
                "Bring it on!"
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
    }
}
