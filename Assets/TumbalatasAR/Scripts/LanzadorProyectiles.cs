#pragma warning disable CS0649

using UnityEngine;
using UnityEngine.InputSystem;

namespace TumbalatasAR
{
    public class LanzadorProyectiles : MonoBehaviour
    {
        public static LanzadorProyectiles Instance { get; private set; }

        [Header("Referencias")]
        [SerializeField] private Camera arCamera;
        [SerializeField] private VistaLanzadorUI vistaUI;

        [Header("Ajustes de Lanzamiento y Balística")]
        [SerializeField] private float distanciaMinimaSwipe = 60f;
        [SerializeField] private float tiempoCargaCompleta = 1.15f;
        [SerializeField] private float fuerzaMinima = 6.0f;
        [SerializeField] private float fuerzaMaxima = 26.0f;
        [SerializeField] private float tiempoVidaProyectil = 6.0f;

        private bool m_CargandoLanzamiento;
        private float m_CargaFuerzaNormalizada;
        private float m_TiempoPresionado;
        private Vector2 m_SwipeInicioPos;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            if (vistaUI == null)
            {
                vistaUI = GetComponent<VistaLanzadorUI>();
                if (vistaUI == null)
                {
                    vistaUI = gameObject.AddComponent<VistaLanzadorUI>();
                }
            }
        }

        private void Start()
        {
            ActualizarVisibilidadUIDisparo(false);
            ActualizarVisualTipoMunicion();
        }

        private void Update()
        {
            bool enJuego = ControladorAR.Instance != null && ControladorAR.Instance.EstadoActual == EstadoJuego.Jugando;

            ActualizarVisibilidadUIDisparo(enJuego);

            if (enJuego)
            {
                ProcesarCargaYLanzamiento();
            }
            else if (m_CargandoLanzamiento)
            {
                CancelarCarga();
            }
        }

        private void ProcesarCargaYLanzamiento()
        {
            Vector2 posActual;

            if (ObtenerPulsacionInicial(out posActual))
            {
                if (ControladorAR.TocoInterfazDeUsuario(posActual))
                {
                    CancelarCarga();
                    return;
                }

                if (vistaUI != null && vistaUI.PosicionEstaEnZonaDisparo(posActual))
                {
                    m_CargandoLanzamiento = true;
                    m_TiempoPresionado = 0f;
                    m_CargaFuerzaNormalizada = 0f;
                    m_SwipeInicioPos = posActual;

                    vistaUI.ActualizarVisualesCarga(0f);
                }
                else
                {
                    CancelarCarga();
                }
                return;
            }

            if (m_CargandoLanzamiento && ObtenerPulsacionMantenida(out posActual))
            {
                m_TiempoPresionado += Time.deltaTime;

                float carga = Mathf.Clamp01(m_TiempoPresionado / tiempoCargaCompleta);

                if (m_TiempoPresionado > tiempoCargaCompleta)
                {
                    float pulso = 0.94f + 0.06f * Mathf.Sin((m_TiempoPresionado - tiempoCargaCompleta) * 14.0f);
                    m_CargaFuerzaNormalizada = pulso;
                }
                else
                {
                    m_CargaFuerzaNormalizada = carga;
                }

                if (vistaUI != null)
                {
                    vistaUI.ActualizarVisualesCarga(m_CargaFuerzaNormalizada);
                }
                return;
            }

            if (m_CargandoLanzamiento && ObtenerPulsacionFinal(out posActual))
            {
                m_CargandoLanzamiento = false;

                Vector2 deltaSwipe = posActual - m_SwipeInicioPos;
                float distanciaSwipe = deltaSwipe.magnitude;

                if (distanciaSwipe >= distanciaMinimaSwipe && deltaSwipe.y > 15f)
                {
                    float deltaTiempo = Mathf.Max(m_TiempoPresionado, 0.04f);
                    float velocidadPantalla = distanciaSwipe / deltaTiempo;

                    EjecutarLanzamientoProyectil(deltaSwipe, velocidadPantalla, m_CargaFuerzaNormalizada);
                }

                CancelarCarga();
            }
        }

        private void EjecutarLanzamientoProyectil(Vector2 deltaSwipe, float velocidadPantalla, float cargaFuerza)
        {
            if (GameManager_Tumbalatas.Instance == null || arCamera == null) return;

            GameObject prefabProyectil = GameManager_Tumbalatas.Instance.ObtenerPrefabProyectilActual();
            if (prefabProyectil == null) return;

            Vector3 origen = arCamera.transform.position + (arCamera.transform.forward * 0.28f) - (arCamera.transform.up * 0.08f);

            float desvioX = (deltaSwipe.x / Screen.width) * 1.5f;
            float inclinacionY = Mathf.Clamp((deltaSwipe.y / Screen.height) * 1.7f, 0.18f, 1.4f);

            Vector3 direccion = (arCamera.transform.forward * 1.25f + arCamera.transform.up * inclinacionY + arCamera.transform.right * desvioX).normalized;

            float fuerzaBase = Mathf.Lerp(fuerzaMinima, fuerzaMaxima, cargaFuerza);
            float bonoVelocidad = Mathf.Clamp(velocidadPantalla * 0.0025f, 0f, 2.5f);
            float fuerzaFinal = Mathf.Clamp(fuerzaBase + bonoVelocidad, fuerzaMinima, fuerzaMaxima + 2.0f);

            GameObject proyectil = Instantiate(prefabProyectil, origen, Quaternion.identity);
            Rigidbody rb = proyectil.GetComponent<Rigidbody>();

            if (rb != null)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.linearVelocity = direccion * fuerzaFinal;
                rb.angularVelocity = arCamera.transform.right * (10.0f + (cargaFuerza * 12.0f));
            }

            GameManager_Tumbalatas.Instance.RegistrarDisparo(proyectil);
            Destroy(proyectil, tiempoVidaProyectil);
        }

        private void CancelarCarga()
        {
            m_CargandoLanzamiento = false;
            m_CargaFuerzaNormalizada = 0f;
            m_TiempoPresionado = 0f;

            if (vistaUI != null)
            {
                vistaUI.CancelarVisualesCarga();
            }
        }

        public void ActualizarVisibilidadUIDisparo(bool visible)
        {
            if (vistaUI != null)
            {
                vistaUI.ActualizarVisibilidad(visible);
            }
        }

        public void ActualizarVisualTipoMunicion()
        {
            if (vistaUI != null)
            {
                TipoProyectil tipo = GameManager_Tumbalatas.Instance != null 
                    ? GameManager_Tumbalatas.Instance.TipoActual 
                    : TipoProyectil.Tenis;

                vistaUI.ActualizarVisualTipoMunicion(tipo);
            }
        }

        private bool ObtenerPulsacionInicial(out Vector2 posicion)
        {
            posicion = Vector2.zero;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                posicion = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                posicion = Mouse.current.position.ReadValue();
                return true;
            }

            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                posicion = Pointer.current.position.ReadValue();
                return true;
            }

            return false;
        }

        private bool ObtenerPulsacionMantenida(out Vector2 posicion)
        {
            posicion = Vector2.zero;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                posicion = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                posicion = Mouse.current.position.ReadValue();
                return true;
            }

            if (Pointer.current != null && Pointer.current.press.isPressed)
            {
                posicion = Pointer.current.position.ReadValue();
                return true;
            }

            return false;
        }

        private bool ObtenerPulsacionFinal(out Vector2 posicion)
        {
            posicion = Vector2.zero;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                posicion = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                posicion = Mouse.current.position.ReadValue();
                return true;
            }

            if (Pointer.current != null && Pointer.current.press.wasReleasedThisFrame)
            {
                posicion = Pointer.current.position.ReadValue();
                return true;
            }

            return false;
        }
    }
}
