using System;
using UnityEngine;

namespace AlicizaX
{
    [Serializable]
    public struct RendererMaterial
    {
        public Renderer meshRenderer;
        public Material material;
        public int materialIndex;

        public bool IsAssigned => meshRenderer != null && material != null;

        public Material ClonedMaterial
        {
            get => material = meshRenderer.materials[materialIndex];
            set
            {
                Material[] materials = meshRenderer.materials;
                materials[materialIndex] = value;
                meshRenderer.materials = materials;
            }
        }
    }
}
