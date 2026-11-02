#pragma warning disable CS0649

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

namespace TumbalatasAR
{
    public enum EstadoJuego
    {
        Escaneando,
        Jugando
    }

    /// <summary>
    /// Controlador central de Realidad Aumentada (AR Foundation):
    /// Gestiona World Tracking, detección y congelamiento de planos horizontales,
    /// retícula de posicionamiento reactiva con feedback visual, colocación estática
    /// con ARAnchor y recálculo limpio de superficies reales.
    /// Segmentado según SRP: la balística de disparo se delega a LanzadorProyectiles.
    /// </summary>
    public class ControladorAR : MonoBehaviour
    {
        public static ControladorAR Instance { get; private set; }

        [Header("Componentes AR Foundation")]
        [SerializeField] private ARRaycastManager arRaycastManager;
        [SerializeField] private ARPlaneManager arPlaneManager;
        [SerializeField] private ARAnchorManager arAnchorManager;
        [SerializeField] private ARSession arSession;
        [SerializeField] private Camera arCamera;

        [Header("Prefabs de Escena")]
        [Tooltip("Indicador visual (retícula) proyectado sobre la superficie detectada")]
        [SerializeField] private GameObject reticlePrefab;

        [Tooltip("Prefab principal de la estructura (Pedestal + Pirámide de Latas)")]
        [SerializeField] private GameObject estructuraPrefab;

        [Header("Subsistema de Lanzamiento")]
        [SerializeField] private LanzadorProyectiles lanzadorProyectiles;

        [Header("Estado Actual")]
        [SerializeField] private EstadoJuego estadoActual = EstadoJuego.Escaneando;

        // Estructuras cacheadas para erradicar recolección de basura (GC) en cada frame
        private static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();
        private static readonly List<RaycastResult> s_UIHits = new List<RaycastResult>();
        private static PointerEventData s_PointerEventData;

        private GameObject m_ReticleInstancia;
        private Vector3 m_EscalaBaseReticula = new Vector3(0.35f, 0.008f, 0.35f);
        private GameObject m_EstructuraInstanciada;
        private ARPlane m_PlanoSeleccionado;

        public EstadoJuego EstadoActual => estadoActual;
        public bool HaySuperficieBajoMira => m_ReticleInstancia != null && m_ReticleInstancia.activeSelf;
        public int CantidadPlanosDetectados => arPlaneManager != null ? arPlaneManager.trackables.count : 0;
        public GameObject EstructuraInstanciada => m_EstructuraInstanciada;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            if (arRaycastManager == null)
            {
                arRaycastManager = FindFirstObjectByType<ARRaycastManager>();
            }

            if (arPlaneManager == null)
            {
                arPlaneManager = FindFirstObjectByType<ARPlaneManager>();
            }

            if (arAnchorManager == null)
            {
                arAnchorManager = FindFirstObjectByType<ARAnchorManager>();
            }

            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>();
            }

            if (lanzadorProyectiles == null)
            {
                lanzadorProyectiles = GetComponent<LanzadorProyectiles>();
                if (lanzadorProyectiles == null)
                {
                    lanzadorProyectiles = gameObject.AddComponent<LanzadorProyectiles>();
                }
            }

            // =========================================================================
            // CALIBRACIÓN CRÍTICA DE ORIGEN Y CÁMARA (RESOLUCIÓN DE OFFSET INVERSO)
            // =========================================================================
            // En AR Móvil (ARCore/ARKit) y simulador XR, el modo Device con altura de ojo
            // posiciona la cámara a 1.35m del piso virtual, garantizando que mesas (Y ~ 0.46m)
            // y suelo (Y ~ 0m) se detecten SIEMPRE por debajo del visor y nunca por encima.
            XROrigin origin = FindFirstObjectByType<XROrigin>();
            if (origin != null)
            {
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                origin.CameraYOffset = 1.35f;

                if (origin.CameraFloorOffsetObject != null)
                {
                    Vector3 posLocal = origin.CameraFloorOffsetObject.transform.localPosition;
                    posLocal.y = 1.35f;
                    origin.CameraFloorOffsetObject.transform.localPosition = posLocal;
                }
            }

            // Aseguramos enlaces nativos de HandheldARInputDevice en TrackedPoseDriver si estuviesen ausentes
            ConfigurarTrackedPoseDriver();

            InicializarReticula();
        }

        private void ConfigurarTrackedPoseDriver()
        {
            if (arCamera == null) return;

            var tpd = arCamera.GetComponent<TrackedPoseDriver>();
            if (tpd != null)
            {
                var posAction = tpd.positionInput.action;
                if (posAction != null && !posAction.bindings.Any(b => b.path.Contains("HandheldARInputDevice")))
                {
                    posAction.AddBinding("<HandheldARInputDevice>/devicePosition");
                }

                var rotAction = tpd.rotationInput.action;
                if (rotAction != null && !rotAction.bindings.Any(b => b.path.Contains("HandheldARInputDevice")))
                {
                    rotAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
                }
            }
        }

        private void InicializarReticula()
        {
            if (reticlePrefab != null)
            {
                m_ReticleInstancia = Instantiate(reticlePrefab);
                m_EscalaBaseReticula = m_ReticleInstancia.transform.localScale;
            }
            else
            {
                // Respaldo dinámico: disco cilíndrico visible en caso de pérdida de referencia en inspector
                m_ReticleInstancia = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                m_ReticleInstancia.transform.localScale = m_EscalaBaseReticula;
                Collider col = m_ReticleInstancia.GetComponent<Collider>();
                if (col != null) Destroy(col); // No debe interferir con físicas
            }

            m_ReticleInstancia.name = "Instancia_Reticula_AR";
            m_ReticleInstancia.SetActive(false);
        }

        private void Start()
        {
            if (EventSystem.current != null)
            {
                s_PointerEventData = new PointerEventData(EventSystem.current);
            }
            estadoActual = EstadoJuego.Escaneando;
        }

        private void Update()
        {
            if (estadoActual == EstadoJuego.Escaneando)
            {
                ActualizarReticulaPosicionamiento();
                ProcesarEntradaParaColocacion();
            }
        }

        // =========================================================================
        // 1. ESCUDO UI (PROTECCIÓN DE CLICK-THROUGH)
        // =========================================================================

        /// <summary>
        /// Comprueba si una coordenada de pantalla colisiona con un elemento interactivo de UI.
        /// Método estático reutilizable por subsistemas auxiliares (LanzadorProyectiles, etc.).
        /// </summary>
        public static bool TocoInterfazDeUsuario(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;

            // 1. Evaluación nativa táctil (primary touch en Android)
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                int touchId = Touchscreen.current.primaryTouch.touchId.ReadValue();
                if (EventSystem.current.IsPointerOverGameObject(touchId)) return true;
            }

            // 2. Evaluación mouse / pointer en Unity Editor y simulador
            if (EventSystem.current.IsPointerOverGameObject()) return true;

            // 3. Respaldo defensivo mediante RaycastAll sobre Canvas (Capa 5 = UI)
            if (s_PointerEventData == null)
            {
                s_PointerEventData = new PointerEventData(EventSystem.current);
            }

            s_PointerEventData.position = screenPosition;
            s_UIHits.Clear();
            EventSystem.current.RaycastAll(s_PointerEventData, s_UIHits);

            for (int i = 0; i < s_UIHits.Count; i++)
            {
                if (s_UIHits[i].gameObject != null && s_UIHits[i].gameObject.layer == 5)
                {
                    return true;
                }
            }

            return false;
        }

        // =========================================================================
        // 2. DETECCIÓN DE PLANOS REALES Y RETÍCULA REACTIVA
        // =========================================================================

        /// <summary>
        /// Evalúa un punto en pantalla contra planos horizontales detectados por AR Foundation.
        /// Cumple estrictamente con la regla de Cátedra: ARRaycastManager es la autoridad exclusiva
        /// para mapeo del suelo real, previniendo colisiones erróneas con capas de entorno o simulación.
        /// </summary>
        public bool ProbarSuperficieHorizontal(Vector2 puntoPantalla, out Vector3 posicion, out Quaternion rotacion, out ARPlane planoDetectado)
        {
            posicion = Vector3.zero;
            rotacion = Quaternion.identity;
            planoDetectado = null;

            if (arCamera == null) return false;

            // Escudo UI: descarta inmediatamente toques sobre botones
            if (TocoInterfazDeUsuario(puntoPantalla)) return false;

            // 1. Detección oficial de AR Foundation (Planos dentro del polígono o sus límites)
            TrackableType tipoPlanos = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds;
            if (arRaycastManager != null && arRaycastManager.Raycast(puntoPantalla, s_Hits, tipoPlanos) && s_Hits.Count > 0)
            {
                posicion = s_Hits[0].pose.position;
                rotacion = s_Hits[0].pose.rotation;
                planoDetectado = arPlaneManager != null ? arPlaneManager.GetPlane(s_Hits[0].trackableId) : null;
                return true;
            }

            // 2. Respaldo defensivo exclusivo de Editor si aún no se generaron mallas AR
            if (Application.isEditor)
            {
                int mascaraCapas = ~((1 << 5) | (1 << 7));
                Ray rayo = arCamera.ScreenPointToRay(puntoPantalla);
                if (Physics.Raycast(rayo, out RaycastHit hitFisico, 20f, mascaraCapas))
                {
                    if (hitFisico.normal.y > 0.65f)
                    {
                        posicion = hitFisico.point;
                        rotacion = Quaternion.FromToRotation(Vector3.up, hitFisico.normal);
                        return true;
                    }
                }
            }

            return false;
        }

        private void ActualizarReticulaPosicionamiento()
        {
            if (m_ReticleInstancia == null) return;

            Vector3 pos;
            Quaternion rot;
            ARPlane plano;

            // A. Editor / Simulador: Seguir el puntero del ratón si está dentro del visor del Game
            if (Application.isEditor && Mouse.current != null)
            {
                Vector2 posMouse = Mouse.current.position.ReadValue();
                if (posMouse.x >= 0 && posMouse.x <= Screen.width && posMouse.y >= 0 && posMouse.y <= Screen.height)
                {
                    if (ProbarSuperficieHorizontal(posMouse, out pos, out rot, out plano))
                    {
                        AplicarReticula(pos, rot, plano);
                        return;
                    }
                }
            }

            // B. Centro de pantalla: Estándar para móviles Android
            Vector2 centroPantalla = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (ProbarSuperficieHorizontal(centroPantalla, out pos, out rot, out plano))
            {
                AplicarReticula(pos, rot, plano);
                return;
            }

            // C. Si no hay superficie bajo la mira, ocultar retícula
            if (m_ReticleInstancia.activeSelf)
            {
                m_ReticleInstancia.SetActive(false);
            }
        }

        private void AplicarReticula(Vector3 pos, Quaternion rot, ARPlane plano)
        {
            m_PlanoSeleccionado = plano;

            // Elevamos 5 mm por encima de la superficie para erradicar Z-fighting con el piso
            Vector3 normalSuperficie = rot * Vector3.up;
            Vector3 posicionElevada = pos + (normalSuperficie * 0.005f);

            m_ReticleInstancia.transform.SetPositionAndRotation(posicionElevada, rot);

            // Feedback visual reactivo: suave pulsación de escala para indicar escaneo activo y receptivo
            float escalaPulsacion = 1f + (Mathf.Sin(Time.time * 5.0f) * 0.04f);
            m_ReticleInstancia.transform.localScale = m_EscalaBaseReticula * escalaPulsacion;

            if (!m_ReticleInstancia.activeSelf)
            {
                m_ReticleInstancia.SetActive(true);
            }
        }

        // =========================================================================
        // 3. COLOCACIÓN DE ESTRUCTURA Y ANCLAJE ESPACIAL
        // =========================================================================

        private void ProcesarEntradaParaColocacion()
        {
            Vector2 posicionToque;
            if (!ObtenerPulsacionInicial(out posicionToque)) return;

            // Escudo UI
            if (TocoInterfazDeUsuario(posicionToque)) return;

            if (estructuraPrefab == null) return;

            Vector3 posicionColocacion = Vector3.zero;
            bool posicionValida = false;

            // 1. Prioridad: toque exacto sobre plano horizontal válido
            if (ProbarSuperficieHorizontal(posicionToque, out Vector3 posToque, out _, out ARPlane planoToque))
            {
                posicionColocacion = posToque;
                m_PlanoSeleccionado = planoToque;
                posicionValida = true;
            }
            // 2. Si se tocó pantalla libre para confirmar y la retícula ya estaba activa
            else if (m_ReticleInstancia != null && m_ReticleInstancia.activeSelf)
            {
                Vector3 normal = m_ReticleInstancia.transform.up;
                posicionColocacion = m_ReticleInstancia.transform.position - (normal * 0.005f);
                posicionValida = true;
            }

            if (posicionValida)
            {
                // Orientamos la estructura hacia la cámara en el plano horizontal
                Vector3 haciaCamara = arCamera.transform.position - posicionColocacion;
                haciaCamara.y = 0;
                Quaternion rotacion = haciaCamara.sqrMagnitude > 0.001f ? Quaternion.LookRotation(haciaCamara) : Quaternion.identity;

                m_EstructuraInstanciada = Instantiate(estructuraPrefab, posicionColocacion, rotacion);

                // Regla Cátedra (Advertencia 1): Anclaje exclusivo para bases estáticas (Pedestal)
                m_EstructuraInstanciada.AddComponent<ARAnchor>();

                // Ocultamos la retícula al colocar el juego
                if (m_ReticleInstancia != null)
                {
                    m_ReticleInstancia.SetActive(false);
                }

                // Regla Cátedra (Advertencia 2): Congelar detección para estabilizar físicas
                CongelarPlanos();

                estadoActual = EstadoJuego.Jugando;

                // Notificamos al GameManager para habilitar la interfaz de juego y registrar latas
                if (GameManager_Tumbalatas.Instance != null)
                {
                    GameManager_Tumbalatas.Instance.RegistrarEstructuraInstanciada(m_EstructuraInstanciada);
                }
            }
        }

        // =========================================================================
        // 4. CONGELAMIENTO, REUBICACIÓN Y RECÁLCULO LIMPIO DE PLANOS
        // =========================================================================

        /// <summary>
        /// Congela la actualización de la malla física de ARPlaneManager para evitar jitter en Rigidbodies.
        /// </summary>
        public void CongelarPlanos()
        {
            if (arPlaneManager == null) return;

            arPlaneManager.enabled = false;

            foreach (var plano in arPlaneManager.trackables)
            {
                if (plano == null) continue;

                var visualizador = plano.GetComponent<ARPlaneMeshVisualizer>();
                if (visualizador != null) visualizador.enabled = false;

                var lineRenderer = plano.GetComponent<LineRenderer>();
                if (lineRenderer != null) lineRenderer.enabled = false;
            }
        }

        /// <summary>
        /// Descongela los planos y elimina la estructura previa para buscar una nueva ubicación.
        /// </summary>
        public void DescongelarYReubicar()
        {
            if (m_EstructuraInstanciada != null)
            {
                Destroy(m_EstructuraInstanciada);
                m_EstructuraInstanciada = null;
            }

            if (arPlaneManager != null)
            {
                arPlaneManager.enabled = true;
                foreach (var plano in arPlaneManager.trackables)
                {
                    if (plano == null) continue;

                    var visualizador = plano.GetComponent<ARPlaneMeshVisualizer>();
                    if (visualizador != null) visualizador.enabled = true;

                    var lineRenderer = plano.GetComponent<LineRenderer>();
                    if (lineRenderer != null) lineRenderer.enabled = true;
                }
            }

            estadoActual = EstadoJuego.Escaneando;
        }

        /// <summary>
        /// Recalcula por completo los planos borrando toda la información previa.
        /// Elimina los GameObjects de planos generados y reinicia la sesión espacial de ARCore/ARSession.
        /// </summary>
        public void RecalcularPlanos()
        {
            // 1. Destruir estructura si existiera
            if (m_EstructuraInstanciada != null)
            {
                Destroy(m_EstructuraInstanciada);
                m_EstructuraInstanciada = null;
            }

            // 2. Ocultar retícula temporalmente
            if (m_ReticleInstancia != null)
            {
                m_ReticleInstancia.SetActive(false);
            }

            // 3. Eliminar objetos visuales de planos existentes
            if (arPlaneManager != null)
            {
                arPlaneManager.enabled = false;
                foreach (var plano in arPlaneManager.trackables)
                {
                    if (plano != null && plano.gameObject != null)
                    {
                        Destroy(plano.gameObject);
                    }
                }
                arPlaneManager.enabled = true;
            }

            // 4. Reiniciar sesión AR Foundation para limpiar el mapa espacial interno
            if (arSession != null)
            {
                arSession.Reset();
            }

            // 5. Retornar al estado de escaneo
            estadoActual = EstadoJuego.Escaneando;
        }

        // =========================================================================
        // 5. ENTRADA DE USUARIO
        // =========================================================================

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
    }
}
