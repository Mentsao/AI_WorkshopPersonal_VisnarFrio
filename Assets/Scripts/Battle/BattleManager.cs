using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PokeBattle
{
    /// <summary>
    /// Orchestrates a Pokemon-style turn-based battle between a player party and
    /// a CPU party. Auto-creates the GroqClient and BattleUI if they aren't
    /// already present, so dropping this single component on a GameObject is
    /// enough to run the game.
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        [Header("Parties (edit in inspector, or leave empty for demo teams)")]
        [SerializeField] private List<Creature> playerParty = new List<Creature>();
        [SerializeField] private List<Creature> enemyParty = new List<Creature>();

        [Header("Pacing")]
        [Tooltip("Pause after each battle-log line.")]
        [SerializeField] private float messageDelay = 1.6f;
        [Tooltip("Minimum time a speech bubble stays on screen so it's readable.")]
        [SerializeField] private float speechReadTime = 3f;
        [Tooltip("Safety cap for how long to wait on a Groq text/voice response.")]
        [SerializeField] private float speechMaxWait = 8f;

        [Header("References (auto-found if left empty)")]
        [SerializeField] private GroqClient groq;
        [SerializeField] private BattleUI ui;

        private int _playerActive;
        private int _enemyActive;

        private Creature PlayerActive => playerParty[_playerActive];
        private Creature EnemyActive => enemyParty[_enemyActive];

        private int _pendingPlayerMove = -1;
        private string _lastSpokenLine = "";

        private void Awake()
        {
            if (groq == null) groq = GetComponent<GroqClient>() ?? gameObject.AddComponent<GroqClient>();
            if (ui == null) ui = FindAnyObjectByType<BattleUI>() ?? CreateUI();

            if (playerParty.Count == 0) playerParty = DemoPlayerParty();
            if (enemyParty.Count == 0) enemyParty = DemoEnemyParty();
        }

        private void Start()
        {
            foreach (var c in playerParty) c.Init();
            foreach (var c in enemyParty) c.Init();

            _playerActive = FirstAlive(playerParty);
            _enemyActive = FirstAlive(enemyParty);

            ui.SetPlayerCreature(PlayerActive);
            ui.SetEnemyCreature(EnemyActive);

            StartCoroutine(BattleLoop());
        }

        private BattleUI CreateUI()
        {
            var go = new GameObject("BattleCanvas", typeof(Canvas));
            return go.AddComponent<BattleUI>();
        }

        // ---------- Main loop ----------

        private IEnumerator BattleLoop()
        {
            ui.AppendLog($"A wild {EnemyActive.name} appeared!");
            ui.AppendLog($"Go, {PlayerActive.name}!");
            yield return new WaitForSeconds(messageDelay);

            while (true)
            {
                // 1) Player chooses a move.
                _pendingPlayerMove = -1;
                ui.ShowMoveMenu(PlayerActive.moves, OnPlayerPickedMove);
                while (_pendingPlayerMove < 0)
                    yield return null;
                ui.HideMoveMenu();

                Move playerMove = PlayerActive.moves[_pendingPlayerMove];

                // 2) CPU chooses a move.
                Move enemyMove = EnemyActive.moves[Random.Range(0, EnemyActive.moves.Count)];

                // 3) Resolve order by speed (player wins ties).
                bool playerFirst = PlayerActive.speed >= EnemyActive.speed;

                if (playerFirst)
                {
                    yield return PerformTurn(true, playerMove);
                    if (BattleOver()) yield break;
                    yield return PerformTurn(false, enemyMove);
                    if (BattleOver()) yield break;
                }
                else
                {
                    yield return PerformTurn(false, enemyMove);
                    if (BattleOver()) yield break;
                    yield return PerformTurn(true, playerMove);
                    if (BattleOver()) yield break;
                }
            }
        }

        private void OnPlayerPickedMove(int index)
        {
            _pendingPlayerMove = index;
        }

        /// <summary>Runs a single attack from one side and resolves faints/switches.</summary>
        private IEnumerator PerformTurn(bool playerIsAttacker, Move move)
        {
            Creature attacker = playerIsAttacker ? PlayerActive : EnemyActive;
            Creature defender = playerIsAttacker ? EnemyActive : PlayerActive;

            if (attacker.IsFainted) yield break;

            ui.AppendLog($"{attacker.name} used {move.name}!");
            yield return new WaitForSeconds(messageDelay * 0.5f);

            bool hit = Random.Range(0, 100) < move.accuracy;
            if (!hit)
            {
                ui.AppendLog($"{attacker.name}'s attack missed!");
                yield return Speak(attacker, playerIsAttacker, "Your attack just missed the target");
                yield return new WaitForSeconds(messageDelay);
                yield break;
            }

            float multiplier = TypeChart.Multiplier(move.type, defender.type);
            int damage = CalculateDamage(move, multiplier);
            defender.TakeDamage(damage);

            if (playerIsAttacker) ui.UpdateEnemyHP(defender);
            else ui.UpdatePlayerHP(defender);

            string effectiveness = TypeChart.Describe(multiplier);
            if (!string.IsNullOrEmpty(effectiveness))
                ui.AppendLog(effectiveness);

            // The attacker taunts based on how the hit landed.
            string situation = multiplier > 1f
                ? $"You just landed a super effective hit on {defender.name}"
                : multiplier < 1f
                    ? $"Your hit on {defender.name} was weak"
                    : $"You hit {defender.name} solidly";
            yield return Speak(attacker, playerIsAttacker, situation);

            yield return new WaitForSeconds(messageDelay);

            if (defender.IsFainted)
            {
                ui.AppendLog($"{defender.name} fainted!");
                yield return new WaitForSeconds(messageDelay);
                yield return HandleFaint(playerIsAttacker);
            }
        }

        /// <summary>The defending side just lost a creature; bring out the next one.</summary>
        private IEnumerator HandleFaint(bool playerWasAttacker)
        {
            if (playerWasAttacker)
            {
                int next = FirstAlive(enemyParty);
                if (next >= 0)
                {
                    _enemyActive = next;
                    ui.SetEnemyCreature(EnemyActive);
                    ui.AppendLog($"The foe sent out {EnemyActive.name}!");
                    yield return new WaitForSeconds(messageDelay);
                }
            }
            else
            {
                int next = FirstAlive(playerParty);
                if (next >= 0)
                {
                    _playerActive = next;
                    ui.SetPlayerCreature(PlayerActive);
                    ui.AppendLog($"Go, {PlayerActive.name}!");
                    yield return new WaitForSeconds(messageDelay);
                }
            }
        }

        /// <summary>Generates and displays a spoken line, waiting (briefly) for Groq.</summary>
        private IEnumerator Speak(Creature speaker, bool isPlayer, string situation)
        {
            Creature opponent = isPlayer ? EnemyActive : PlayerActive;
            ui.ShowSpeech(speaker.name, "...", isPlayer);

            string result = null;
            groq.Speak(speaker.name, speaker.type, opponent.name, situation, _lastSpokenLine, line => result = line);

            float elapsed = 0f;
            while (result == null && elapsed < speechMaxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (result == null) result = "...";
            _lastSpokenLine = result;
            ui.ShowSpeech(speaker.name, result, isPlayer);
            ui.AppendLog($"{speaker.name}: \"{result}\"");

            // Voice the line and keep the bubble up until BOTH the audio has
            // finished AND it has been visible long enough to comfortably read.
            bool spoken = false;
            groq.PlaySpeech(result, () => spoken = true);

            float minShow = speechReadTime + ReadingBonus(result);
            float maxShow = speechMaxWait + minShow + 6f;
            float shown = 0f;
            while ((!spoken || shown < minShow) && shown < maxShow)
            {
                shown += Time.deltaTime;
                yield return null;
            }

            ui.HideSpeech();
            yield return new WaitForSeconds(messageDelay * 0.3f);
        }

        // ---------- Helpers ----------

        private int CalculateDamage(Move move, float multiplier)
        {
            // Lightweight formula: power * type multiplier * small random spread.
            float spread = Random.Range(0.85f, 1.0f);
            int dmg = Mathf.RoundToInt(move.power * multiplier * spread * 0.5f);
            return Mathf.Max(1, dmg);
        }

        /// <summary>Extra on-screen time for longer lines (~3 words per second).</summary>
        private static float ReadingBonus(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0f;
            int words = line.Split(' ').Length;
            return Mathf.Clamp(words / 3f, 0f, 4f);
        }

        private bool BattleOver()
        {
            if (FirstAlive(enemyParty) < 0)
            {
                ui.HideMoveMenu();
                ui.AppendLog("You won the battle!");
                return true;
            }
            if (FirstAlive(playerParty) < 0)
            {
                ui.HideMoveMenu();
                ui.AppendLog("You were defeated...");
                return true;
            }
            return false;
        }

        private static int FirstAlive(List<Creature> party)
        {
            for (int i = 0; i < party.Count; i++)
                if (!party[i].IsFainted) return i;
            return -1;
        }

        // ---------- Demo data ----------

        private static List<Creature> DemoPlayerParty()
        {
            return new List<Creature>
            {
                new Creature
                {
                    name = "Embertail", type = ElementType.Fire, maxHP = 120, speed = 70,
                    moves = new List<Move>
                    {
                        new Move("Ember", 40, ElementType.Fire),
                        new Move("Flame Burst", 70, ElementType.Fire, 90),
                        new Move("Scratch", 35, ElementType.Grass),
                        new Move("Aqua Tail", 60, ElementType.Water, 85),
                    }
                },
                new Creature
                {
                    name = "Leafling", type = ElementType.Grass, maxHP = 110, speed = 55,
                    moves = new List<Move>
                    {
                        new Move("Vine Whip", 45, ElementType.Grass),
                        new Move("Leaf Blade", 70, ElementType.Grass, 90),
                        new Move("Splash Hit", 50, ElementType.Water),
                    }
                },
                new Creature
                {
                    name = "Bublet", type = ElementType.Water, maxHP = 115, speed = 60,
                    moves = new List<Move>
                    {
                        new Move("Water Gun", 45, ElementType.Water),
                        new Move("Hydro Pump", 80, ElementType.Water, 80),
                        new Move("Spark Bite", 50, ElementType.Fire),
                    }
                },
            };
        }

        private static List<Creature> DemoEnemyParty()
        {
            return new List<Creature>
            {
                new Creature
                {
                    name = "Aquath", type = ElementType.Water, maxHP = 120, speed = 65,
                    moves = new List<Move>
                    {
                        new Move("Bubble", 40, ElementType.Water),
                        new Move("Surf", 70, ElementType.Water, 90),
                        new Move("Bite", 45, ElementType.Grass),
                    }
                },
                new Creature
                {
                    name = "Pyrokit", type = ElementType.Fire, maxHP = 110, speed = 75,
                    moves = new List<Move>
                    {
                        new Move("Ember", 40, ElementType.Fire),
                        new Move("Fire Fang", 65, ElementType.Fire, 90),
                        new Move("Leaf Toss", 50, ElementType.Grass),
                    }
                },
                new Creature
                {
                    name = "Thornox", type = ElementType.Grass, maxHP = 125, speed = 50,
                    moves = new List<Move>
                    {
                        new Move("Razor Leaf", 45, ElementType.Grass),
                        new Move("Solar Slam", 80, ElementType.Grass, 80),
                        new Move("Flare Up", 50, ElementType.Fire),
                    }
                },
            };
        }
    }
}
