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
        [SerializeField] private GameObject reticlePrefab;
        [SerializeField] private GameObject estructuraPrefab;

        [Header("Subsistema de Lanzamiento")]
        [SerializeField] private LanzadorProyectiles lanzadorProyectiles;

        [Header("Filtro Anti-Subzonas Coplanares")]
        [SerializeField] private float umbralAlturaCoplanar = 0.04f;
        [SerializeField] private float intervaloFiltradoPlanos = 0.2f;

        [Header("Estado Actual")]
        [SerializeField] private EstadoJuego estadoActual = EstadoJuego.Escaneando;

        private static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();
        private static readonly List<RaycastResult> s_UIHits = new List<RaycastResult>();
        private static PointerEventData s_PointerEventData;

        private readonly List<ARPlane> m_PlanosActivosCache = new List<ARPlane>();
        private readonly HashSet<TrackableId> m_SubzonasOcultas = new HashSet<TrackableId>();
        private float m_TiempoSiguienteFiltrado;

        private GameObject m_ReticleInstancia;
        private Vector3 m_EscalaBaseReticula = new Vector3(0.35f, 0.008f, 0.35f);
        private GameObject m_EstructuraInstanciada;
        private ARPlane m_PlanoSeleccionado;

        public EstadoJuego EstadoActual => estadoActual;
        public bool HaySuperficieBajoMira => m_ReticleInstancia != null && m_ReticleInstancia.activeSelf;
        public int CantidadPlanosDetectados
        {
            get
            {
                if (arPlaneManager == null) return 0;
                int conteo = 0;
                foreach (var plano in arPlaneManager.trackables)
                {
                    if (plano != null && plano.subsumedBy == null && plano.alignment == PlaneAlignment.HorizontalUp && !m_SubzonasOcultas.Contains(plano.trackableId))
                    {
                        conteo++;
                    }
                }
                return conteo;
            }
        }
        public GameObject EstructuraInstanciada => m_EstructuraInstanciada;
        public LanzadorProyectiles Lanzador => lanzadorProyectiles;

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

            if (arPlaneManager != null)
            {
                arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
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
                m_ReticleInstancia = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                m_ReticleInstancia.transform.localScale = m_EscalaBaseReticula;
                Collider col = m_ReticleInstancia.GetComponent<Collider>();
                if (col != null) Destroy(col);
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

                m_TiempoSiguienteFiltrado -= Time.deltaTime;
                if (m_TiempoSiguienteFiltrado <= 0f)
                {
                    m_TiempoSiguienteFiltrado = intervaloFiltradoPlanos;
                    FiltrarSubZonasCoplanares();
                }
            }
        }

        public static bool TocoInterfazDeUsuario(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                int touchId = Touchscreen.current.primaryTouch.touchId.ReadValue();
                if (EventSystem.current.IsPointerOverGameObject(touchId)) return true;
            }

            if (EventSystem.current.IsPointerOverGameObject()) return true;

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

        public bool ProbarSuperficieHorizontal(Vector2 puntoPantalla, out Vector3 posicion, out Quaternion rotacion, out ARPlane planoDetectado)
        {
            posicion = Vector3.zero;
            rotacion = Quaternion.identity;
            planoDetectado = null;

            if (arCamera == null) return false;

            if (TocoInterfazDeUsuario(puntoPantalla)) return false;

            TrackableType tipoPlanos = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds;
            if (arRaycastManager != null && arRaycastManager.Raycast(puntoPantalla, s_Hits, tipoPlanos) && s_Hits.Count > 0)
            {
                int mejorHit = -1;
                float mayorArea = -1f;

                for (int i = 0; i < s_Hits.Count; i++)
                {
                    ARPlane plano = arPlaneManager != null ? arPlaneManager.GetPlane(s_Hits[i].trackableId) : null;
                    if (plano == null || plano.subsumedBy != null) continue;
                    if (plano.alignment != PlaneAlignment.HorizontalUp) continue;

                    Vector3 normalHit = s_Hits[i].pose.rotation * Vector3.up;
                    if (Vector3.Dot(normalHit, Vector3.up) < 0.95f) continue;

                    bool esSubzona = m_SubzonasOcultas.Contains(s_Hits[i].trackableId);
                    float area = plano.size.x * plano.size.y;

                    if (!esSubzona && area > mayorArea)
                    {
                        mayorArea = area;
                        mejorHit = i;
                    }
                }

                if (mejorHit >= 0)
                {
                    posicion = s_Hits[mejorHit].pose.position;
                    rotacion = s_Hits[mejorHit].pose.rotation;
                    planoDetectado = arPlaneManager != null ? arPlaneManager.GetPlane(s_Hits[mejorHit].trackableId) : null;
                    return true;
                }
            }

            if (Application.isEditor)
            {
                int mascaraCapas = ~((1 << 5) | (1 << 7));
                Ray rayo = arCamera.ScreenPointToRay(puntoPantalla);
                if (Physics.Raycast(rayo, out RaycastHit hitFisico, 20f, mascaraCapas))
                {
                    if (Vector3.Dot(hitFisico.normal, Vector3.up) >= 0.96f)
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

            Vector2 centroPantalla = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (ProbarSuperficieHorizontal(centroPantalla, out pos, out rot, out plano))
            {
                AplicarReticula(pos, rot, plano);
                return;
            }

            if (m_ReticleInstancia.activeSelf)
            {
                m_ReticleInstancia.SetActive(false);
            }
        }

        private void AplicarReticula(Vector3 pos, Quaternion rot, ARPlane plano)
        {
            m_PlanoSeleccionado = plano;

            Vector3 normalSuperficie = rot * Vector3.up;
            Vector3 posicionElevada = pos + (normalSuperficie * 0.005f);

            m_ReticleInstancia.transform.SetPositionAndRotation(posicionElevada, rot);

            float escalaPulsacion = 1f + (Mathf.Sin(Time.time * 5.0f) * 0.04f);
            m_ReticleInstancia.transform.localScale = m_EscalaBaseReticula * escalaPulsacion;

            if (!m_ReticleInstancia.activeSelf)
            {
                m_ReticleInstancia.SetActive(true);
            }
        }

        private void FiltrarSubZonasCoplanares()
        {
            if (arPlaneManager == null) return;

            m_PlanosActivosCache.Clear();

            foreach (var plano in arPlaneManager.trackables)
            {
                if (plano != null && plano.subsumedBy == null && plano.trackingState != TrackingState.None)
                {
                    if (plano.alignment != PlaneAlignment.HorizontalUp)
                    {
                        SilenciarPlanoVisual(plano, true);
                        continue;
                    }

                    m_PlanosActivosCache.Add(plano);
                }
            }

            if (m_PlanosActivosCache.Count <= 1) return;

            for (int i = 0; i < m_PlanosActivosCache.Count; i++)
            {
                var planoB = m_PlanosActivosCache[i];
                if (planoB == null) continue;

                bool esSubzona = false;

                for (int j = 0; j < m_PlanosActivosCache.Count; j++)
                {
                    if (i == j) continue;
                    var planoA = m_PlanosActivosCache[j];
                    if (planoA == null) continue;

                    float deltaY = Mathf.Abs(planoA.center.y - planoB.center.y);
                    if (deltaY > umbralAlturaCoplanar) continue;

                    float areaA = planoA.size.x * planoA.size.y;
                    float areaB = planoB.size.x * planoB.size.y;
                    if (areaA <= areaB) continue;

                    Vector2 posA = new Vector2(planoA.center.x, planoA.center.z);
                    Vector2 posB = new Vector2(planoB.center.x, planoB.center.z);
                    float dist2D = Vector2.Distance(posA, posB);

                    float radioA = Mathf.Max(planoA.size.x, planoA.size.y) * 0.65f;
                    if (dist2D < radioA)
                    {
                        esSubzona = true;
                        break;
                    }
                }

                if (esSubzona)
                {
                    SilenciarPlanoVisual(planoB, true);
                    m_SubzonasOcultas.Add(planoB.trackableId);
                }
                else if (m_SubzonasOcultas.Contains(planoB.trackableId))
                {
                    SilenciarPlanoVisual(planoB, false);
                    m_SubzonasOcultas.Remove(planoB.trackableId);
                }
            }
        }

        private void SilenciarPlanoVisual(ARPlane plano, bool silenciar)
        {
            if (plano == null) return;

            var visualizador = plano.GetComponent<ARPlaneMeshVisualizer>();
            if (visualizador != null) visualizador.enabled = !silenciar;

            var lineRenderer = plano.GetComponent<LineRenderer>();
            if (lineRenderer != null) lineRenderer.enabled = !silenciar;

            var meshRenderer = plano.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = !silenciar;
        }

        private void ProcesarEntradaParaColocacion()
        {
            Vector2 posicionToque;
            if (!ObtenerPulsacionInicial(out posicionToque)) return;

            if (TocoInterfazDeUsuario(posicionToque)) return;

            if (estructuraPrefab == null) return;

            Vector3 posicionColocacion = Vector3.zero;
            bool posicionValida = false;

            if (ProbarSuperficieHorizontal(posicionToque, out Vector3 posToque, out _, out ARPlane planoToque))
            {
                posicionColocacion = posToque;
                m_PlanoSeleccionado = planoToque;
                posicionValida = true;
            }
            else if (m_ReticleInstancia != null && m_ReticleInstancia.activeSelf)
            {
                Vector3 normal = m_ReticleInstancia.transform.up;
                posicionColocacion = m_ReticleInstancia.transform.position - (normal * 0.005f);
                posicionValida = true;
            }

            if (posicionValida)
            {
                Vector3 haciaCamara = arCamera.transform.position - posicionColocacion;
                haciaCamara.y = 0;
                Quaternion rotacion = haciaCamara.sqrMagnitude > 0.001f ? Quaternion.LookRotation(haciaCamara) : Quaternion.identity;

                m_EstructuraInstanciada = Instantiate(estructuraPrefab, posicionColocacion, rotacion);
                m_EstructuraInstanciada.AddComponent<ARAnchor>();

                if (m_ReticleInstancia != null)
                {
                    m_ReticleInstancia.SetActive(false);
                }

                CongelarPlanos();

                estadoActual = EstadoJuego.Jugando;

                if (GameManager_Tumbalatas.Instance != null)
                {
                    GameManager_Tumbalatas.Instance.RegistrarEstructuraInstanciada(m_EstructuraInstanciada);
                }
            }
        }

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

        public void DescongelarYReubicar()
        {
            m_SubzonasOcultas.Clear();

            if (m_EstructuraInstanciada != null)
            {
                Destroy(m_EstructuraInstanciada);
                m_EstructuraInstanciada = null;
            }

            if (arPlaneManager != null)
            {
                arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                arPlaneManager.enabled = true;
                foreach (var plano in arPlaneManager.trackables)
                {
                    if (plano == null) continue;
                    if (plano.alignment != PlaneAlignment.HorizontalUp) continue;

                    var visualizador = plano.GetComponent<ARPlaneMeshVisualizer>();
                    if (visualizador != null) visualizador.enabled = true;

                    var lineRenderer = plano.GetComponent<LineRenderer>();
                    if (lineRenderer != null) lineRenderer.enabled = true;
                }
            }

            estadoActual = EstadoJuego.Escaneando;
        }

        public void RecalcularPlanos()
        {
            m_SubzonasOcultas.Clear();

            if (m_EstructuraInstanciada != null)
            {
                Destroy(m_EstructuraInstanciada);
                m_EstructuraInstanciada = null;
            }

            if (m_ReticleInstancia != null)
            {
                m_ReticleInstancia.SetActive(false);
            }

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
                arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                arPlaneManager.enabled = true;
            }

            if (arSession != null)
            {
                arSession.Reset();
            }

            estadoActual = EstadoJuego.Escaneando;
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
    }
}
