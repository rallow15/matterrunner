using System.Collections.Generic;
using UnityEngine;

namespace Runner
{
    /// <summary>
    /// ParticleBurst — Pool de particules cubes, portage du burst() du prototype web.
    ///
    /// Rôle :
    ///  - Burst(pos, couleur, count, power) : fait éclater N petits cubes colorés.
    ///  - 80 cubes max en scène (pool STRICT : aucun Instantiate/Destroy en jeu).
    ///  - Gravité −9, durée de vie 0.7–1.0 s, rotation aléatoire (comme le web).
    ///
    /// À attacher sur : un GameObject vide "Particles" de la scène.
    /// Compatible Unity 2022+.
    /// </summary>
    public class ParticleBurst : MonoBehaviour
    {
        [Header("=== Pool ===")]
        [Tooltip("Nombre maximum de particules simultanées (le web : 80)")]
        public int poolSize = 80;

        [Tooltip("Taille d'un cube de particule (le web : 0.2)")]
        public float particleSize = 0.2f;

        [Header("=== Physique (valeurs du web) ===")]
        public float gravity = -9f;

        public float lifeMin = 0.7f;
        public float lifeMax = 1.0f;

        // ---- État interne ----
        private readonly List<Particle> pool = new List<Particle>();

        private class Particle
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Material material; // instance INDIVIDUELLE : chaque particule a sa propre teinte
            public Transform tr;
            public Vector3 velocity;
            public Vector3 spin;      // rotation aléatoire (le web : rotation)
            public float life;
            public float maxLife;
        }

        private void Start()
        {
            // Base de matériau partagé (pour le shader) — chaque particule aura son instance.
            var baseMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            baseMaterial.name = "ParticleBaseMaterial";

            for (int i = 0; i < poolSize; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "particle_" + i;
                Object.Destroy(cube.GetComponent<Collider>());   // décoration pure : zéro collision

                var r = cube.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                // Une instance par particule : les teintes ne se battent pas, et
                // le getter .material ne crée JAMAIS de copie tardive en jeu (zéro GC spike).
                var mat = new Material(baseMaterial);
                r.material = mat;

                cube.transform.SetParent(transform, false);
                cube.SetActive(false);

                pool.Add(new Particle
                {
                    go = cube,
                    renderer = r,
                    material = mat,
                    tr = cube.transform,
                    velocity = Vector3.zero,
                    spin = Vector3.zero,
                    life = 0f,
                    maxLife = 1f,
                });
            }
        }

        private void Update()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (!p.go.activeSelf) continue;

                // Physique : montée, gravité, rotation.
                p.velocity.y += gravity * Time.deltaTime;
                p.tr.position += p.velocity * Time.deltaTime;
                p.tr.Rotate(p.spin * Time.deltaTime, Space.Self);

                // Durée de vie : rétrécit puis repart au pool.
                p.life -= Time.deltaTime;
                float shrink = Mathf.Clamp01(p.life / p.maxLife);
                float s = particleSize * shrink;
                p.tr.localScale = new Vector3(s, s, s);

                if (p.life <= 0f) p.go.SetActive(false);
            }
        }

        /// <summary>
        /// Fait éclater des particules. Exemples du web :
        ///     Burst(pos, bleu, 10, 3)   (éclaboussure eau)
        ///     Burst(pos, jaune, 14, 3.5) (mur brisé)
        /// </summary>
        public void Burst(Vector3 position, Color color, int count, float power)
        {
            int spawned = 0;
            for (int i = 0; i < pool.Count && spawned < count; i++)
            {
                var p = pool[i];
                if (p.go.activeSelf) continue;   // particule déjà en vol : on la laisse

                p.go.SetActive(true);
                p.tr.position = position;
                p.tr.localScale = Vector3.one * particleSize;

                // Vitesse aléatoire en demi-sphère vers le haut (comme le web : cos/sin aléatoires).
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float up = Random.Range(0.4f, 1f);
                float speed = Random.Range(power * 0.5f, power);
                p.velocity = new Vector3(Mathf.Cos(angle) * speed * up, speed, Mathf.Sin(angle) * speed * up * 0.6f);

                // Rotation aléatoire (le feel "débris").
                p.spin = new Vector3(Random.Range(-8f, 8f), Random.Range(-8f, 8f), Random.Range(-8f, 8f));

                p.maxLife = Random.Range(lifeMin, lifeMax);
                p.life = p.maxLife;

                // Teinte (URP/Unlit : _BaseColor) — via l'instance de la particule.
                if (p.material.HasProperty("_BaseColor"))
                    p.material.SetColor("_BaseColor", color);
                else
                    p.material.color = color;

                spawned++;
            }
        }
    }
}