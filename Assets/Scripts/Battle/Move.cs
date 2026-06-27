using UnityEngine;

namespace PokeBattle
{
    [System.Serializable]
    public class Move
    {
        public string name = "Tackle";
        public int power = 40;
        public ElementType type = ElementType.Fire;

        [Range(0, 100)]
        public int accuracy = 100;

        public Move() { }

        public Move(string name, int power, ElementType type, int accuracy = 100)
        {
            this.name = name;
            this.power = power;
            this.type = type;
            this.accuracy = accuracy;
        }
    }
}
