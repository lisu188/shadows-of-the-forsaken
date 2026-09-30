using UnityEngine;

namespace ShadowsOfTheForsaken.Combat
{
    // Deliberately small placeholder feedback. Property blocks avoid allocating
    // material instances or changing shared art assets.
    [DisallowMultipleComponent, RequireComponent(typeof(CombatHealth))]
    public sealed class CombatFeedback : MonoBehaviour
    {
        public Renderer[] visuals;
        public Color windupColor = new Color(1, .65f, .08f);
        public Color activeColor = new Color(1, .18f, .05f);
        public Color hitColor = Color.red;
        public Color deadColor = new Color(.18f, .18f, .18f);
        private CombatHealth health;
        private MeleeCombat melee;
        private MaterialPropertyBlock[] originals;
        private Color[] baseColors;
        private MaterialPropertyBlock working;
        private float flash;

        private void OnEnable()
        {
            // Unity may deserialize this component off the main thread. Create
            // native-backed blocks in the lifecycle callback, then reuse them.
            if (working == null) working = new MaterialPropertyBlock();
            health = GetComponent<CombatHealth>(); melee = GetComponent<MeleeCombat>();
            if (visuals == null || visuals.Length == 0) visuals = GetComponentsInChildren<Renderer>();
            originals = new MaterialPropertyBlock[visuals.Length];
            baseColors = new Color[visuals.Length];
            for (int i = 0; i < visuals.Length; i++)
            {
                originals[i] = new MaterialPropertyBlock();
                if (visuals[i] == null) continue;
                visuals[i].GetPropertyBlock(originals[i]);
                var material = visuals[i].sharedMaterial;
                baseColors[i] = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                    material != null && material.HasProperty("_Color") ? material.color : Color.white;
            }
            health.Changed += HealthChanged;
            Apply();
        }
        private void OnDisable()
        {
            if (health != null) health.Changed -= HealthChanged;
            if (originals != null)
                for (int i = 0; i < originals.Length; i++) if (visuals[i] != null) visuals[i].SetPropertyBlock(originals[i]);
            flash = 0;
        }
        private void HealthChanged(HealthChange change)
        {
            flash = change.IsReset ? 0 : .16f;
            Apply();
        }
        private void Update() { flash = Mathf.Max(0, flash - Time.deltaTime); Apply(); }
        private void Apply()
        {
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] == null) continue;
                Color color = !health.IsAlive ? deadColor : flash > 0 ? hitColor :
                    melee != null && melee.Phase == AttackPhase.Windup ? windupColor :
                    melee != null && melee.Phase == AttackPhase.Active ? activeColor : baseColors[i];
                visuals[i].GetPropertyBlock(working);
                working.SetColor("_BaseColor", color); working.SetColor("_Color", color);
                visuals[i].SetPropertyBlock(working);
            }
        }
    }
}
