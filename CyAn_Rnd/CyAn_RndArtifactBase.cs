using BepInEx.Configuration;
using RoR2;
using R2API;
using UnityEngine;
using IL.RoR2.ContentManagement;

namespace CyAn_Rnd
{
    public abstract class CyAn_RndArtifactBase
    {
        public ArtifactDef ArtifactDef;

        public abstract string ArtifactName { get; }
        public abstract string ArtifactLangTokenName { get; }
        public abstract string ArtifactDescription { get; }
        public abstract Sprite ArtifactEnabledIcon { get; }
        public abstract Sprite ArtifactDisabledIcon { get; }

        public bool ArtifactEnabled => RunArtifactManager.instance?.IsArtifactEnabled(ArtifactDef) ?? false;

        public abstract void Init(ConfigFile config);

        protected void CreateLang()
        {
            LanguageAPI.Add("ARTIFACT_" + ArtifactLangTokenName + "_NAME", ArtifactName);
            LanguageAPI.Add("ARTIFACT_" + ArtifactLangTokenName + "_DESCRIPTION", ArtifactDescription);
        }

        protected void CreateArtifact()
        {
            ArtifactDef = ScriptableObject.CreateInstance<ArtifactDef>();
            ArtifactDef.cachedName = "ARTIFACT_" + ArtifactLangTokenName;
            ArtifactDef.nameToken = "ARTIFACT_" + ArtifactLangTokenName + "_NAME";
            ArtifactDef.descriptionToken = "ARTIFACT_" + ArtifactLangTokenName + "_DESCRIPTION";
            ArtifactDef.smallIconSelectedSprite = ArtifactEnabledIcon;
            ArtifactDef.smallIconDeselectedSprite = ArtifactDisabledIcon;

            ContentAddition.AddArtifactDef(ArtifactDef);
        }

        public abstract void Hooks();
    }
}
