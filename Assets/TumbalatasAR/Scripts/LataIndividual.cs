using UnityEngine;

namespace TumbalatasAR
{
    [RequireComponent(typeof(Rigidbody))]
    public class LataIndividual : MonoBehaviour
    {
        [SerializeField] private float umbralInclinacion = 0.65f;
        [SerializeField] private float distanciaCaida = 0.05f;

        private Rigidbody m_Rigidbody;
        private Vector3 m_LocalPosInicial;
        private Quaternion m_LocalRotInicial;
        private bool m_HaSidoDerribada;

        public bool HaSidoDerribada => m_HaSidoDerribada;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_LocalPosInicial = transform.localPosition;
            m_LocalRotInicial = transform.localRotation;
            m_HaSidoDerribada = false;
        }

        private void Start()
        {
            if (m_Rigidbody != null)
            {
                m_Rigidbody.Sleep();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (m_Rigidbody != null && m_Rigidbody.IsSleeping())
            {
                m_Rigidbody.WakeUp();
            }
        }

        private void Update()
        {
            if (!m_HaSidoDerribada)
            {
                bool inclinada = Vector3.Dot(transform.up, Vector3.up) < umbralInclinacion;
                bool cayo = transform.localPosition.y < (m_LocalPosInicial.y - distanciaCaida);

                if (inclinada || cayo)
                {
                    m_HaSidoDerribada = true;
                }
            }
        }

        public void ReiniciarLata()
        {
            m_HaSidoDerribada = false;

            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;

            transform.localPosition = m_LocalPosInicial;
            transform.localRotation = m_LocalRotInicial;

            m_Rigidbody.Sleep();
        }
    }
}
