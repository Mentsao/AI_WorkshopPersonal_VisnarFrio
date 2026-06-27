namespace PokeBattle
{
    /// <summary>
    /// Simple rock-paper-scissors type chart: Fire > Grass > Water > Fire.
    /// </summary>
    public static class TypeChart
    {
        public const float SuperEffective = 2f;
        public const float NotVeryEffective = 0.5f;
        public const float Neutral = 1f;

        public static float Multiplier(ElementType attacker, ElementType defender)
        {
            if (attacker == defender)
                return Neutral;

            bool strong =
                (attacker == ElementType.Fire && defender == ElementType.Grass) ||
                (attacker == ElementType.Grass && defender == ElementType.Water) ||
                (attacker == ElementType.Water && defender == ElementType.Fire);

            return strong ? SuperEffective : NotVeryEffective;
        }

        public static string Describe(float multiplier)
        {
            if (multiplier > Neutral) return "It's super effective!";
            if (multiplier < Neutral) return "It's not very effective...";
            return string.Empty;
        }
    }
}
