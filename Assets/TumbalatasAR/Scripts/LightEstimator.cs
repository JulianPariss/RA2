using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace TumbalatasAR
{
    [RequireComponent(typeof(Light))]
    public class LightEstimator : MonoBehaviour
    {
        [Header("Referencias AR")]
        [SerializeField] private ARCameraManager arCameraManager;

        [Header("Ajustes de Intensidad")]
        [Range(0.1f, 3.0f)]
        [SerializeField] private float multiplicadorBrillo = 1.0f;
        [SerializeField] private float intensidadMinima = 0.4f;
        [SerializeField] private float intensidadMaxima = 2.5f;

        [Header("Ajustes de Rotación de Luz")]
        [SerializeField] private bool seguirDireccionLuz = true;
        [SerializeField] private float velocidadSuavizadoRotacion = 3.0f;

        private Light m_LuzDireccional;

        private void Awake()
        {
            m_LuzDireccional = GetComponent<Light>();
            m_LuzDireccional.color = Color.white;
            m_LuzDireccional.useColorTemperature = false;

            if (arCameraManager == null)
            {
                arCameraManager = FindFirstObjectByType<ARCameraManager>();
            }
        }

        private void OnEnable()
        {
            if (arCameraManager != null)
            {
                arCameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable()
        {
            if (arCameraManager != null)
            {
                arCameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (eventArgs.lightEstimation.averageBrightness.HasValue)
            {
                float brillo = eventArgs.lightEstimation.averageBrightness.Value;
                float intensidadCalculada = Mathf.Clamp(brillo * multiplicadorBrillo, intensidadMinima, intensidadMaxima);
                m_LuzDireccional.intensity = intensidadCalculada;
            }
            else if (eventArgs.lightEstimation.averageIntensityInLumens.HasValue)
            {
                float lumens = eventArgs.lightEstimation.averageIntensityInLumens.Value;
                float intensidadNormalizada = Mathf.Clamp(lumens / 1000f, intensidadMinima, intensidadMaxima);
                m_LuzDireccional.intensity = intensidadNormalizada;
            }

            if (seguirDireccionLuz && eventArgs.lightEstimation.mainLightDirection.HasValue)
            {
                Vector3 direccionLuz = eventArgs.lightEstimation.mainLightDirection.Value;
                Quaternion rotacionDeseada = Quaternion.LookRotation(direccionLuz);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotacionDeseada, Time.deltaTime * velocidadSuavizadoRotacion);
            }
        }
    }
}
