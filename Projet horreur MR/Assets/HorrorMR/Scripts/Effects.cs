using UnityEngine;

namespace HorrorMR
{
    /// <summary>Effets visuels créés à la volée : sang, rembourrage, étincelles, flash, flaque de sang.</summary>
    public static class Effects
    {
        static HorrorAssets s_Assets;

        public static void Init(HorrorAssets assets) => s_Assets = assets;

        static ParticleSystem CreateSystem(string name, Vector3 position, Vector3 direction, Material material)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.rotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction.normalized) : Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ps.Play();
            Object.Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax + 0.5f);
        }

        static void FadeOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;
        }

        public static void BloodBurst(Vector3 position, Vector3 direction, float scale)
        {
            if (s_Assets == null || s_Assets.bloodParticle == null)
                return;
            var ps = CreateSystem("Sang", position, direction, s_Assets.bloodParticle);
            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 3.5f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.01f, 0.01f), new Color(0.25f, 0f, 0f));
            main.gravityModifier = 1.2f;
            main.maxParticles = 200;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.03f;
            FadeOut(ps);
            Burst(ps, Mathf.RoundToInt(35 * scale));
        }

        public static void StuffingBurst(Vector3 position, Vector3 direction, bool big)
        {
            if (s_Assets == null || s_Assets.stuffingParticle == null)
                return;
            var ps = CreateSystem("Rembourrage", position, direction, s_Assets.stuffingParticle);
            var main = ps.main;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, big ? 5f : 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, big ? 2.2f : 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, big ? 0.13f : 0.08f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.82f, 0.78f), new Color(0.6f, 0.52f, 0.5f));
            main.gravityModifier = 0.12f;
            main.maxParticles = 300;
            var shape = ps.shape;
            shape.shapeType = big ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = big ? 0.25f : 0.04f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 2.5f;
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            FadeOut(ps);
            Burst(ps, big ? 140 : 18);
        }

        public static void Sparks(Vector3 position, Vector3 normal)
        {
            if (s_Assets == null || s_Assets.sparkParticle == null)
                return;
            var ps = CreateSystem("Impact", position, normal, s_Assets.sparkParticle);
            var main = ps.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.5f, 0.15f));
            main.gravityModifier = 0.8f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.01f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1f;
            Burst(ps, 14);
        }

        public static void MuzzleFlash(Transform muzzle)
        {
            if (s_Assets == null || s_Assets.muzzleFlash == null || muzzle == null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Flash";
            go.transform.SetParent(muzzle, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.04f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * Random.Range(0.12f, 0.18f);
            go.GetComponent<MeshRenderer>().sharedMaterial = s_Assets.muzzleFlash;

            var side = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(side.GetComponent<Collider>());
            side.transform.SetParent(go.transform, false);
            side.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            side.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            side.transform.localScale = new Vector3(1.6f, 0.6f, 1f);
            side.GetComponent<MeshRenderer>().sharedMaterial = s_Assets.muzzleFlash;

            var lightGo = new GameObject("Lumière flash");
            lightGo.transform.SetParent(muzzle, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.range = 6f;
            light.intensity = 6f;

            Object.Destroy(go, 0.05f);
            Object.Destroy(lightGo, 0.06f);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            if (s_Assets == null || s_Assets.laser == null)
                return;
            var go = new GameObject("Traçante");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = s_Assets.laser;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.008f;
            line.endWidth = 0.003f;
            line.startColor = new Color(1f, 0.85f, 0.6f, 0.9f);
            line.endColor = new Color(1f, 0.6f, 0.3f, 0.2f);
            line.useWorldSpace = true;
            Object.Destroy(go, 0.035f);
        }

        public static void BloodPool(Vector3 floorPosition)
        {
            if (s_Assets == null || s_Assets.bloodPool == null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Flaque de sang";
            go.transform.position = floorPosition + Vector3.up * 0.012f;
            go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            go.GetComponent<MeshRenderer>().sharedMaterial = s_Assets.bloodPool;
            go.AddComponent<GrowOverTime>().Configure(0.15f, 1.6f, 5f);
        }
    }
}
