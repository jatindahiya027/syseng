using System;
using System.Collections;
using System.Collections.Generic;
using MadGoat.Core.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if SSAA_URP
#if UNITY_2019_3_OR_NEWER
using UnityEngine.Rendering.Universal;
#elif UNITY_2019_1_OR_NEWER
using UnityEngine.Rendering.LWRP;
#endif
#endif

#if SSAA_HDRP
#if UNITY_2019_3_OR_NEWER
using UnityEngine.Rendering.HighDefinition;
#elif UNITY_2019_1_OR_NEWER
using UnityEngine.Experimental.Rendering.HDPipeline;  
#endif
#endif

namespace MadGoat.SSAA {
    [RequireComponent(typeof(Camera))]
    public class MadGoatSSAA_VR : MadGoatSSAA {

#if UNITY_2019_4_OR_NEWER
        List<UnityEngine.XR.XRDisplaySubsystem> vrDisplays = new List<UnityEngine.XR.XRDisplaySubsystem>();
#if UNITY_EDITOR
        UnityEditor.PackageManager.Requests.ListRequest packmanRequest;
#endif
#endif

        #region Properties
        public PostAntiAliasingMode SsaaUltraOld { get; set; }
        private float VRCachedRenderScale { get; set; }
        #endregion

        #region MonoBehaviour Event Methods
        private void Start() {
            if (Initialized) return;
            OnInitializeProps();
        }
        private void OnEnable() {
#if UNITY_EDITOR
#if UNITY_2019_4_OR_NEWER
            // Check for unity XR management package
            packmanRequest = UnityEditor.PackageManager.Client.List();    // List packages installed for the project
            EditorApplication.update += VRManagerPackmanCheck;
#else
            SymbolDefineUtils.RemoveDefine("SSAA_XR");
#endif
            if (!EditorApplication.isPlaying) return;
#endif

            // Handle original scaling
            // Handle the resolution multiplier
            if (!VRDeviceAnyActive()) throw new Exception("VRDevice not present or not detected");

            // Cache old resolution before using SSAA .This solves issues with more exotic 
            // headsets that use non 1x scaling by default such as Playstation VR
            VRDeviceCacheRes();

            // Decide if we initialize or just re-enable
            if (!Initialized) Start();
            else if (Pipeline == RenderPipelineUtils.PipelineType.BuiltInPipeline) SetupDownsamplerCommandBuffer();

            // Adaptive res startup
            StartCoroutine(UpdateAdaptiveRes());
        }
        private void Update() {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying) return;
#endif

            // Adaptive Res Update 
            if (PerfSampler != null) PerfSampler.Update();
            else PerfSampler = new SsaaFramerateSampler();

            // Update the materials properties
            if (Pipeline == RenderPipelineUtils.PipelineType.BuiltInPipeline && Initialized) {
                // Change the material by the filter type
                ChangeMaterial(DownsamplerFilter);
                UpdateDownsamplerCommandBuffer();
            }

#if SSAA_HDRP || SSAA_URP
            OnBeginCameraRender(CurrentCamera);
#endif
        }
        private void OnDisable() {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying) return;
#endif

            // Handle VR device cleaning - reset to original scaling
            VRDeviceUpdate(VRCachedRenderScale);

            // Handle command buffer cleaning
            if (Pipeline == RenderPipelineUtils.PipelineType.BuiltInPipeline) {
                ClearDownsamplerCommandBuffer();
            }
        }
        #endregion

        #region SSAA Event Implementation
        public override void OnBeginCameraRender(Camera cam) {
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying) return;
#endif  
            if (cam != currentCamera || !enabled || !Initialized || !VRDeviceAnyActive()) return;

            // Handle the resolution multiplier  
            VRDeviceUpdate(InternalRenderMultiplier);

#if SSAA_HDRP
            if (InternalRenderMultiplier > 1 && PostAntiAliasing == PostAntiAliasingMode.FSSAA && InternalRenderMode != RenderMode.AdaptiveResolution) {
                HdCamDataCurrent.antialiasing = HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing;
            }
            else if (InternalRenderMultiplier > 1 && PostAntiAliasing == PostAntiAliasingMode.TSSAA && InternalRenderMode != RenderMode.AdaptiveResolution) {
                HdCamDataCurrent.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            }
            else {
                HdCamDataCurrent.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            }
#endif

#if SSAA_URP && UNITY_2019_3_OR_NEWER
            if (InternalRenderMultiplier > 1 && PostAntiAliasing == PostAntiAliasingMode.FSSAA && InternalRenderMode != RenderMode.AdaptiveResolution) {
                UniversalCamDataCurrent.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            }
            else {
                UniversalCamDataCurrent.antialiasing = AntialiasingMode.None;
            }
#endif 
        }
        protected override void OnInitialize() {
            if (currentCamera == null)
                currentCamera = GetComponent<Camera>();

            // fix for VR black screen
            MaterialBicubic.SetOverrideTag("RenderType", "Opaque");
            MaterialBicubic.SetInt("_SrcBlend", (int)BlendMode.One);
            MaterialBicubic.SetInt("_DstBlend", (int)BlendMode.Zero);
            MaterialBicubic.SetInt("_ZWrite", 1);
            MaterialBicubic.renderQueue = -1;

            MaterialBilinear.SetOverrideTag("RenderType", "Opaque");
            MaterialBilinear.SetInt("_SrcBlend", (int)BlendMode.One);
            MaterialBilinear.SetInt("_DstBlend", (int)BlendMode.Zero);
            MaterialBilinear.SetInt("_ZWrite", 1);
            MaterialBilinear.renderQueue = -1;

            MaterialDefault.SetOverrideTag("RenderType", "Opaque");
            MaterialDefault.SetInt("_SrcBlend", (int)BlendMode.One);
            MaterialDefault.SetInt("_DstBlend", (int)BlendMode.Zero);
            MaterialDefault.SetInt("_ZWrite", 1);
            MaterialDefault.renderQueue = -1;

            MaterialCurrent = MaterialDefault;
            MaterialOld = MaterialCurrent;

            if (Pipeline == RenderPipelineUtils.PipelineType.BuiltInPipeline)
                SetupDownsamplerCommandBuffer();
#if SSAA_URP && UNITY_2019_3_OR_NEWER
            UniversalCamDataCurrent = GetComponent<UniversalAdditionalCameraData>(); 
            if (!UniversalCamDataCurrent) UniversalCamDataCurrent = gameObject.AddComponent<UniversalAdditionalCameraData>();
#elif SSAA_URP
            LwrpCamDataCurrent = GetComponent<LWRPAdditionalCameraData>(); 
            if (!LwrpCamDataCurrent) LwrpCamDataCurrent = gameObject.AddComponent<LWRPAdditionalCameraData>();
#elif SSAA_HDRP
            HdCamDataCurrent = GetComponent<HDAdditionalCameraData>();
            if (!HdCamDataCurrent) HdCamDataCurrent = gameObject.AddComponent<HDAdditionalCameraData>();
#endif
            Initialized = true;
        }
        protected void ChangeMaterial(Filter Type) {

            // Point material_current to the given material
            switch (Type) {
                case Filter.POINT:
                    MaterialCurrent = MaterialDefault;
                    break;
                case Filter.BILINEAR:
                    MaterialCurrent = MaterialBilinear;
                    break;
                case Filter.BICUBIC:
                    MaterialCurrent = MaterialBicubic;
                    break;
            }

            // Hanle the correct pass
            if ((!DownsamplerEnabled || InternalRenderMultiplier == 1) && MaterialCurrent != MaterialDefault) {
                MaterialCurrent = MaterialDefault;
            }

            // if material must be changed we have to reset the command buffer
            if (MaterialCurrent != MaterialOld || SsaaUltraOld != PostAntiAliasing) {
                // avoid updating for invalid VR 
                if (!VRDeviceAnyActive()) return;

                SsaaUltraOld = PostAntiAliasing;
                MaterialOld = MaterialCurrent;
                ClearDownsamplerCommandBuffer();
                SetupDownsamplerCommandBuffer();
            }
        }
        #endregion

        #region MadGoat SSAA Standard Pipeline Core Implementation 
        #region CB Setup
        protected void SetupDownsamplerCommandBuffer() {
            if (CbDownsampler == null) CbDownsampler = new CommandBuffer();

            // avoid updating for invalid VR 
            if (!VRDeviceAnyActive()) return;

            if ((new List<CommandBuffer>(currentCamera.GetCommandBuffers(constCamEventDownsampler))).Find(x => x.name == "SSAA_VR_APPLY") == null) {
                // set up buffer rt
                if (RtDownsampler) RtDownsampler.Release(); 
                RtDownsampler = new RenderTexture(1024, 1024, 24, InternalTextureFormat); 
                RenderTargetIdentifier ssaaCommandBufferTargetId = new RenderTargetIdentifier(RtDownsampler);

                // Fix for singlepass issue in u2018 and newer
#if UNITY_2017_2_OR_NEWER
                RtDownsampler.vrUsage = UnityEngine.XR.XRSettings.eyeTextureDesc.vrUsage;
#endif

                // Command buffer setup
                CbDownsampler.Clear();
                CbDownsampler.name = "SSAA_VR_APPLY";
                CbDownsampler.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

                // Blits for doownsampling and apply to camera target
                CbDownsampler.Blit(BuiltinRenderTextureType.CameraTarget, ssaaCommandBufferTargetId, MaterialCurrent, 0);
                CbDownsampler.Blit(ssaaCommandBufferTargetId, BuiltinRenderTextureType.CameraTarget, InternalRenderMultiplier > 1 && PostAntiAliasing == PostAntiAliasingMode.FSSAA && InternalRenderMode != RenderMode.AdaptiveResolution ? MaterialFXAA : MaterialDefault, 0);

                // Register cb to camera
                currentCamera.AddCommandBuffer(constCamEventDownsampler, CbDownsampler);
            }
        }
        protected void UpdateDownsamplerCommandBuffer() {
            RtDownsampler.Release();

#if UNITY_2017_2_OR_NEWER
            if (UnityEngine.XR.XRSettings.eyeTextureWidth > 0 && UnityEngine.XR.XRSettings.eyeTextureHeight != 0) {
                if (RtDownsampler.width != UnityEngine.XR.XRSettings.eyeTextureWidth * 2 ||
                    RtDownsampler.height != UnityEngine.XR.XRSettings.eyeTextureHeight) { 

                    RtDownsampler.Release();
                    RtDownsampler.width = UnityEngine.XR.XRSettings.eyeTextureWidth * 2;
                    RtDownsampler.height = UnityEngine.XR.XRSettings.eyeTextureHeight;
                    RtDownsampler.Create();
                }
            }
#else
            if (UnityEngine.XR.XRSettings.eyeTextureWidth > 0 && UnityEngine.XR.XRSettings.eyeTextureHeight > 0) {
                if (RtDownsampler.width != UnityEngine.VR.VRSettings.eyeTextureWidth ||
                    RtDownsampler.height != UnityEngine.VR.VRSettings.eyeTextureHeight) {

                    RtDownsampler.Release();
                    RtDownsampler.width = UnityEngine.VR.VRSettings.eyeTextureWidth;
                    RtDownsampler.height = UnityEngine.VR.VRSettings.eyeTextureHeight;
                    RtDownsampler.Create();
                }
            }
#endif

            // fix for VR black screen
            MaterialCurrent.SetOverrideTag("RenderType", "Opaque");
            MaterialCurrent.SetInt("_SrcBlend", (int)BlendMode.One);
            MaterialCurrent.SetInt("_DstBlend", (int)BlendMode.Zero);
            MaterialCurrent.SetInt("_ZWrite", 1);
            MaterialCurrent.renderQueue = -1;

            MaterialDefault.SetOverrideTag("RenderType", "Opaque");
            MaterialDefault.SetInt("_SrcBlend", (int)BlendMode.One);
            MaterialDefault.SetInt("_DstBlend", (int)BlendMode.Zero);
            MaterialDefault.SetInt("_ZWrite", 1);
            MaterialDefault.renderQueue = -1;

#if UNITY_2017_2_OR_NEWER
            MaterialCurrent.SetFloat("_ResizeWidth", UnityEngine.XR.XRSettings.eyeTextureWidth);
            MaterialCurrent.SetFloat("_ResizeHeight", UnityEngine.XR.XRSettings.eyeTextureHeight);
#else
            MaterialCurrent.SetFloat("_ResizeWidth", UnityEngine.VR.VRSettings.eyeTextureWidth);
            MaterialCurrent.SetFloat("_ResizeHeight", UnityEngine.VR.VRSettings.eyeTextureHeight);
#endif
            MaterialCurrent.SetFloat("_Sharpness", DownsamplerSharpness);
            MaterialCurrent.SetFloat("_SampleDistance", DownsamplerDistance);

            MaterialFXAA.SetVector("_QualitySettings", new Vector3(1.0f, 0.063f, 0.0312f));
            MaterialFXAA.SetVector("_ConsoleSettings", new Vector4(0.5f, 2.0f, 0.125f, 0.04f));
            MaterialFXAA.SetFloat("_Intensity", 1);
        }
        protected void ClearDownsamplerCommandBuffer() {
            if (currentCamera == null) return;
            if ((new List<CommandBuffer>(currentCamera.GetCommandBuffers(constCamEventDownsampler))).Find(x => x.name == "SSAA_VR_APPLY") != null) {
                currentCamera.RemoveCommandBuffer(constCamEventDownsampler, CbDownsampler);
            }
        }
        #endregion
        #region VR Device API
#if UNITY_2019_4_OR_NEWER
        protected void VRDeviceFetch() {
            vrDisplays.Clear();
            SubsystemManager.GetInstances(vrDisplays);
        }
#endif
        protected bool VRDeviceAnyActive() {
            // 2019.4 LTS +
#if UNITY_2019_4_OR_NEWER 
            VRDeviceFetch();
            for (int i = 0; i < vrDisplays.Count; i++) {
                if (vrDisplays[i].running) {
                    return true;
                }
            }
            return false;

            // Legacy VR system
#elif UNITY_2017_2_OR_NEWER
            return UnityEngine.XR.XRDevice.isPresent;
#else
            return UnityEngine.VR.VRDevice.isPresent; 
#endif
        }
        protected void VRDeviceUpdate(float renderScale) {
            // Fallback for invalid values
            if (renderScale < .1f) renderScale = 1;

            // Handle High Level VR Render Res (Legacy Support, does nothing in SRP)
#if UNITY_2017_2_OR_NEWER
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = renderScale;
#else
            UnityEngine.VR.VRSettings.renderScale = renderScale;
#endif

            // Handle Low Level VR Render Res (2019.4 LTS+)
#if UNITY_2020_1_OR_NEWER && SSAA_XR
            // For update, do not fetch devices again. Suppose we already fetched in the same call,
            // when we checked for active devices. If we reach this we already fetched once.
            for (int i = 0; i < vrDisplays.Count; i++) {
                if (vrDisplays[i] == null) continue;
                vrDisplays[i].scaleOfAllRenderTargets = renderScale;
            }
#endif
        }
        protected void VRDeviceCacheRes() {
#if UNITY_2020_1_OR_NEWER
            VRCachedRenderScale = UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
#if SSAA_HDRP && SSAA_XR
            // For cache, do not fetch devices again. Suppose we already fetched in the same call,
            // when we checked for active devices. If we reach this we already fetched once.
            for (int i = 0; i < vrDisplays.Count; i++) {
                if (vrDisplays[i].running) {
                    VRCachedRenderScale = vrDisplays[i].scaleOfAllRenderTargets;
                    break;
                }
            }
#endif
#elif UNITY_2017_2_OR_NEWER
            VRCachedRenderScale = UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
#else
            VRCachedRenderScale = UnityEngine.VR.VRSettings.renderScale;
#endif
        }
        protected void VRManagerPackmanCheck() {
#if UNITY_EDITOR && UNITY_2019_4_OR_NEWER
            if (packmanRequest.IsCompleted) {
                var found = false;
                // On success check if xr management is installed
                if (packmanRequest.Status == UnityEditor.PackageManager.StatusCode.Success) {
                    foreach (var package in packmanRequest.Result) {
                        if (package.name.Contains("xr.management")) found = true;
                    }
                }

                // Decide wether or not to add define
                if (found) SymbolDefineUtils.AddDefine("SSAA_XR");
                else {
                    SymbolDefineUtils.RemoveDefine("SSAA_XR");
                    Debug.LogError("XR Management package not found. SSAA Requires it to be installed on Unity 2019.4+");
                }
                EditorApplication.update -= VRManagerPackmanCheck;
            }
#endif
        }
        #endregion
        #endregion

        #region MadGoat SSAA Public API Implementation
        #region Instance API
        /// <summary>
        /// Set the multiplier of each screen axis independently. does not use downsampling filter.
        /// </summary>
        public override void SetAsAxisBased(float MultiplierX, float MultiplierY) {
            Debug.LogWarning("SetAsAxisBased is not supported in VR.\nX axis will be used as global multiplier instead.");
            base.SetAsAxisBased(MultiplierX, MultiplierY);
        }
        /// <summary>
        ///  Set the multiplier of each screen axis independently while using the downsampling filter.
        /// </summary>
        public override void SetAsAxisBased(float MultiplierX, float MultiplierY, Filter FilterType, float sharpnessfactor, float sampledist) {
            Debug.LogWarning("SetAsAxisBased is not supported in VR.\nX axis will be used as global multiplier instead.");
            base.SetAsAxisBased(MultiplierX, MultiplierY, FilterType, sharpnessfactor, sampledist);
        }

        /// <summary>
        /// Take a screenshot of resolution Size (x is width, y is height) rendered at a higher resolution given by the multiplier. The screenshot is saved at the given path in PNG format.
        /// </summary>
        public override void TakeScreenshot(string path, Vector2 Size, int multiplier) {
            Debug.LogWarning("Not available in VR mode");
        }
        /// <summary>
        /// Take a screenshot of resolution Size (x is width, y is height) rendered at a higher resolution given by the multiplier. The screenshot is saved at the given path in PNG format. Uses given post process AA method on top of SSAA
        /// </summary>
        public override void TakeScreenshot(string path, Vector2 Size, int multiplier, PostAntiAliasingMode postAntiAliasing) {
            Debug.LogWarning("Not available in VR mode");
        }
        /// <summary>
        /// Take a screenshot of resolution Size (x is width, y is height) rendered at a higher resolution given by the multiplier. The screenshot is saved at the given path in PNG format.
        /// </summary>
        public override void TakeScreenshot(string path, Vector2 size, int multiplier, Filter filterType, float sharpness, float sampleDistance) {
            Debug.LogWarning("Not available in VR mode");
        }
        /// <summary>
        /// Take a screenshot of resolution Size (x is width, y is height) rendered at a higher resolution given by the multiplier. The screenshot is saved at the given path in PNG format. Uses given post process AA method on top of SSAA.
        /// </summary>
        public override void TakeScreenshot(string path, Vector2 size, int multiplier, Filter filterType, float sharpness, float sampleDistance, PostAntiAliasingMode postAntiAliasing) {
            Debug.LogWarning("Not available in VR mode");
        }

        /// <summary>
        /// Sets up the screenshot module to use the PNG image format. This enables transparency in output images.
        /// </summary>
        public override void SetScreenshotModuleToPNG() {
            Debug.LogWarning("Not available in VR mode");
        }
        /// <summary>
        /// Sets up the screenshot module to use the JPG image format. Quality is parameter from 1 to 100 and represents the compression quality of the JPG file. Incorrect quality values will be clamped.
        /// </summary>
        /// <param name="quality"></param>
        public override void SetScreenshotModuleToJPG(int quality) {
            Debug.LogWarning("Not available in VR mode");
        }
#if UNITY_5_6_OR_NEWER
        /// <summary>
        /// Sets up the screenshot module to use the EXR image format. The EXR32 bool parameter dictates whether to use or not 32 bit exr encoding. This method is only available in Unity 5.6 and newer.
        /// </summary>
        /// <param name="EXR32"></param>
        public override void SetScreenshotModuleToEXR(bool EXR32) {
            Debug.LogWarning("Not available in VR mode");
        }
#endif
        #endregion

        #region Deprecated - Will be removed in the future
        /// <summary>
        /// Take a screenshot of resolution Size (x is width, y is height) rendered at a higher resolution given by the multiplier and use the bicubic downsampler. The screenshot is saved at the given path in PNG format. 
        /// </summary>
        [System.Obsolete()]
        public override void TakeScreenshot(string path, Vector2 Size, int multiplier, float sharpness) {
            Debug.LogWarning("Not available in VR mode");
        }
        /// <summary>
        /// Returns a ray from a given screenpoint
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        [Obsolete("SSAA ScreenPointToRay has been deprecated. Use Camera's API instead")]
        public override Ray ScreenPointToRay(Vector3 position) {
            return currentCamera.ScreenPointToRay(position);
        }
        /// <summary>
        /// Transforms position from screen space into world space
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        [Obsolete("SSAA ScreenToWorldPoint has been deprecated. Use Camera's API instead")]
        public override Vector3 ScreenToWorldPoint(Vector3 position) {
            return currentCamera.ScreenToWorldPoint(position);
        }
        /// <summary>
        /// Transforms postion from screen space into viewport space.
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        [Obsolete("SSAA ScreenToViewportPoint has been deprecated. Use Camera's API instead")]
        public override Vector3 ScreenToViewportPoint(Vector3 position) {
            return currentCamera.ScreenToViewportPoint(position);
        }
        /// <summary>
        /// Transforms position from world space to screen space
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        [Obsolete("SSAA WorldToScreenPoint has been deprecated. Use Camera's API instead")]
        public override Vector3 WorldToScreenPoint(Vector3 position) {
            return currentCamera.WorldToScreenPoint(position);
        }
        /// <summary>
        /// Transforms position from viewport space to screen space
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        [Obsolete("SSAA ViewportToScreenPoint has been deprecated. Use Camera's API instead")]
        public override Vector3 ViewportToScreenPoint(Vector3 position) {
            return currentCamera.ViewportToScreenPoint(position);
        }
        #endregion
        #endregion
    }
}