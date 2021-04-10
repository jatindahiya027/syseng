using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MadGoat.SSAA;

namespace MadGoat.SSAA.Demo {
    public class DemoGUI : MonoBehaviour {
        private MadGoatSSAA ssaa;
        private bool mode = false; // 0 ssaa 1 res
        private bool ultra;
        private float multiplier = 100f;
        private GUIStyle s;
        // Use this for initialization
        void Start() {
            ssaa = FindObjectOfType<MadGoatSSAA>();
        }
        float deltaTime = 0.0f;

        void Update() {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
        }

        private void OnGUI() {
            GUI.contentColor = new Color(0, 0, 0);
            float msec = deltaTime * 1000.0f;
            float fps = 1.0f / deltaTime;
            string text = string.Format("{0:0.0} ms ({1:0.} fps)", msec, fps);
            GUI.Label(new Rect(Screen.width - 150, 10, 150, 50), text);
            if (GUI.Button(new Rect(20, 10, 120, 20), !mode ? "Switch to scaling" : "Switch to ssaa")) {
                mode = !mode;
            }
            if (mode) {
                GUI.Label(new Rect(20, 50, 100, 20), ((int)multiplier).ToString() + "%");
                multiplier = GUI.HorizontalSlider(new Rect(55, 55, 100, 20), multiplier, 50, 200);
                    ssaa.SetAsScale((int)multiplier, MadGoat.SSAA.Filter.BICUBIC, 0.8f, 0.7f);
            }
            else {
                if (GUI.Button(new Rect(20, 50, 80, 20), "off")) {
                    ssaa.SetAsSSAA(SSAAMode.SSAA_OFF);
                }
                if (GUI.Button(new Rect(20, 75, 80, 20), "x0.5")) {
                    ssaa.SetAsSSAA(SSAAMode.SSAA_HALF);
                }
                if (GUI.Button(new Rect(20, 100, 80, 20), "x2")) {
                    ssaa.SetAsSSAA(SSAAMode.SSAA_X2);
                }
                if (GUI.Button(new Rect(20, 125, 80, 20), "x4")) {
                    ssaa.SetAsSSAA(SSAAMode.SSAA_X4);
                }
            }
            GUI.contentColor = new Color(0, 0, 0);
            ultra = GUI.Toggle(new Rect(20, 150, 150, 20), ultra, "Ultra Quality (FSSAA)");
            ssaa.SetPostAAMode(ultra ? PostAntiAliasingMode.FSSAA : PostAntiAliasingMode.Off);
        } 
    }
}