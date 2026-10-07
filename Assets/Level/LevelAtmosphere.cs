using System.Collections.Generic;
using UnityEngine;

namespace ShadowsOfTheForsaken.Level
{
    public sealed class LevelAtmosphere : MonoBehaviour
    {
        private List<Light> torches;
        private AudioClip ambience;
        public void Configure(List<Light> torchLights)
        {
            torches = torchLights;
            // Original synthesized ambience: wind and low stone resonance, no external audio assets.
            const int sampleRate = 22050, count = sampleRate * 8;
            var samples = new float[count];
            var random = new System.Random(741);
            float filtered = 0;
            for (int i = 0; i < count; i++)
            {
                filtered = filtered * .96f + ((float)random.NextDouble() * 2 - 1) * .04f;
                float t = i / (float)sampleRate;
                // Fade both boundaries to zero for a seam-free loop.
                float envelope = Mathf.Sin(Mathf.PI * i / (count - 1));
                samples[i] = envelope * (filtered * .32f + Mathf.Sin(t * Mathf.PI * 2 * 55) * .035f);
            }
            ambience = AudioClip.Create("Wind through the ruins", count, 1, sampleRate, false);
            ambience.SetData(samples, 0);
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = ambience; source.loop = true; source.spatialBlend = 0; source.volume = .45f; source.Play();
        }

        private void Update()
        {
            if (torches == null) return;
            for (int i = 0; i < torches.Count; i++)
                if (torches[i] != null) torches[i].intensity = 3.1f + .23f * Mathf.Sin(Time.time * 8.3f + i * 2.1f) + .13f * Mathf.Sin(Time.time * 17.1f + i);
        }
        private void OnDestroy() { if (ambience != null) Destroy(ambience); }
    }
}
