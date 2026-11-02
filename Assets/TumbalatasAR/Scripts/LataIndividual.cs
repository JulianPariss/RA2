using UnityEngine;

namespace TumbalatasAR
{
    /// <summary>
    /// Controla el estado físico y derribo de cada lata individual en la pirámide.
    /// Respeta la estructura Padre-Hijo y permite el reinicio sin asignación de memoria.
    /// PROHIBIDO asignar ARAnchor a este componente (cuerpo dinámico con Rigidbody).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class LataIndividual : MonoBehaviour
    {
        [Header("Configuración de Detección")]
        [Tooltip("Umbral de inclinación para considerar la lata derribada (producto punto vertical < umbral)")]
        [SerializeField] private float umbralInclinacion = 0.65f; // Aprox 49 grados de inclinación

        [Tooltip("Distancia de caída por debajo de la base para considerarla derribada")]
        [SerializeField] private float distanciaCaida = 0.05f;

        private Rigidbody m_Rigidbody;
        private Vector3 m_LocalPosInicial;
        private Quaternion m_LocalRotInicial;
        private bool m_HaSidoDerribada;

        public bool HaSidoDerribada => m_HaSidoDerribada;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();

            // Guardamos la pose local inicial con respecto al contenedor de la pirámide
            m_LocalPosInicial = transform.localPosition;
            m_LocalRotInicial = transform.localRotation;
            m_HaSidoDerribada = false;
        }

        private void Start()
        {
            // Ponemos a dormir el cuerpo en reposo para que la pirámide quede 100% estable al crearse
            if (m_Rigidbody != null)
            {
                m_Rigidbody.Sleep();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Al recibir impacto (proyectil u otra lata), aseguramos que despierte
            if (m_Rigidbody != null && m_Rigidbody.IsSleeping())
            {
                m_Rigidbody.WakeUp();
            }
        }

        private void Update()
        {
            if (!m_HaSidoDerribada)
            {
                // Evaluamos si se inclinó o si cayó fuera del pedestal
                bool inclinada = Vector3.Dot(transform.up, Vector3.up) < umbralInclinacion;
                bool cayo = transform.localPosition.y < (m_LocalPosInicial.y - distanciaCaida);

                if (inclinada || cayo)
                {
                    m_HaSidoDerribada = true;
                }
            }
        }

        /// <summary>
        /// Restablece la lata a su estado original sobre la pirámide sin instanciaciones ni recolección de basura.
        /// </summary>
        public void ReiniciarLata()
        {
            m_HaSidoDerribada = false;

            // En Unity 6 se usa linearVelocity, reseteamos ambas velocidades
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;

            transform.localPosition = m_LocalPosInicial;
            transform.localRotation = m_LocalRotInicial;

            // Ponemos a dormir el cuerpo un instante para evitar micro-rebotes al reaparecer
            m_Rigidbody.Sleep();
        }
    }
}
