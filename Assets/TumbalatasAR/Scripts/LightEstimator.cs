using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace TumbalatasAR
{
    /// <summary>
    /// Gestiona la adaptacion de la iluminacion virtual a la luz ambiental real.
    /// Cumple las directivas de catedra: suscripcion limpia en OnEnable/OnDisable (sin memory leaks)
    /// y mantenimiento del color en blanco neutro puro para prevenir parpadeos en el piso real.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightEstimator : MonoBehaviour
    {
        [Header("Referencias AR")]
        [Tooltip("Referencia al ARCameraManager ubicado en la AR Camera")]
        [SerializeField] private ARCameraManager arCameraManager;

        [Header("Ajustes de Intensidad")]
        [Tooltip("Multiplicador para calibrar la intensidad lumínica ambiental")]
        [Range(0.1f, 3.0f)]
        [SerializeField] private float multiplicadorBrillo = 1.0f;

        [Tooltip("Intensidad mínima para evitar que la escena quede completamente a oscuras")]
        [SerializeField] private float intensidadMinima = 0.4f;

        [Tooltip("Intensidad máxima permitida para evitar sobreexposición")]
        [SerializeField] private float intensidadMaxima = 2.5f;

        [Header("Ajustes de Rotación de Luz")]
        [Tooltip("Alinear la dirección de la luz con la dirección solar estimada (si está disponible)")]
        [SerializeField] private bool seguirDireccionLuz = true;

        [Tooltip("Velocidad de interpolación de la rotación de la luz")]
        [SerializeField] private float velocidadSuavizadoRotacion = 3.0f;

        private Light m_LuzDireccional;

        private void Awake()
        {
            m_LuzDireccional = GetComponent<Light>();

            // Regla de cátedra: Mantener la luz direccional en blanco neutro puro
            // para evitar parpadeos y virajes cromáticos molestos en el material transparente del piso.
            m_LuzDireccional.color = Color.white;
            m_LuzDireccional.useColorTemperature = false;

            if (arCameraManager == null)
            {
                arCameraManager = FindFirstObjectByType<ARCameraManager>();
            }
        }

        private void OnEnable()
        {
            // Suscripción al evento de cuadros de cámara
            if (arCameraManager != null)
            {
                arCameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable()
        {
            // Regla crítica de cátedra: Des-suscripción obligatoria para evitar Memory Leaks en el GC
            if (arCameraManager != null)
            {
                arCameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            // 1. Modulación de intensidad lumínica
            if (eventArgs.lightEstimation.averageBrightness.HasValue)
            {
                float brillo = eventArgs.lightEstimation.averageBrightness.Value;
                float intensidadCalculada = Mathf.Clamp(brillo * multiplicadorBrillo, intensidadMinima, intensidadMaxima);
                m_LuzDireccional.intensity = intensidadCalculada;
            }
            else if (eventArgs.lightEstimation.averageIntensityInLumens.HasValue)
            {
                // En ARCore algunos dispositivos informan directamente en lúmenes
                float lumens = eventArgs.lightEstimation.averageIntensityInLumens.Value;
                float intensidadNormalizada = Mathf.Clamp(lumens / 1000f, intensidadMinima, intensidadMaxima);
                m_LuzDireccional.intensity = intensidadNormalizada;
            }

            // 2. Modulación de dirección (sombras orientadas)
            if (seguirDireccionLuz && eventArgs.lightEstimation.mainLightDirection.HasValue)
            {
                Vector3 direccionLuz = eventArgs.lightEstimation.mainLightDirection.Value;
                Quaternion rotacionDeseada = Quaternion.LookRotation(direccionLuz);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotacionDeseada, Time.deltaTime * velocidadSuavizadoRotacion);
            }
        }
    }
}
