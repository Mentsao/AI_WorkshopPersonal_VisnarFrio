using System.Collections.Generic;
using UnityEngine;

namespace PokeBattle
{
    [System.Serializable]
    public class Creature
    {
        public string name = "Sparky";
        public ElementType type = ElementType.Fire;
        public int maxHP = 100;
        public int speed = 50;

        public List<Move> moves = new List<Move>();

        // Runtime state (not meant to be hand-edited in the inspector).
        [HideInInspector] public int currentHP;

        public bool IsFainted => currentHP <= 0;

        /// <summary>Resets the creature to full health. Call before a battle starts.</summary>
        public void Init()
        {
            currentHP = maxHP;
        }

        public void TakeDamage(int amount)
        {
            currentHP = Mathf.Clamp(currentHP - amount, 0, maxHP);
        }

        public float HealthRatio => maxHP <= 0 ? 0f : (float)currentHP / maxHP;
    }
}
