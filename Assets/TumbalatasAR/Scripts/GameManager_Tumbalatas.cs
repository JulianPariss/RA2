#pragma warning disable CS0649

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace TumbalatasAR
{
    public enum TipoProyectil
    {
        Tenis,
        Acero
    }

    /// <summary>
    /// Administrador de la lógica de juego, marcador, munición y estados de la interfaz.
    /// Controla la transición limpia entre el modo de escaneo y el modo de juego activo,
    /// ocultando botones de tiro y reseteo hasta que el escenario esté posicionado.
    /// Proporciona feedback dinámico durante la búsqueda de planos y permite su recálculo.
    /// </summary>
    public class GameManager_Tumbalatas : MonoBehaviour
    {
        public static GameManager_Tumbalatas Instance { get; private set; }

        [Header("Controlador Principal")]
        [SerializeField] private ControladorAR controladorAR;

        [Header("Prefabs de Proyectiles")]
        [Tooltip("Prefab de pelota de tenis (liviana y elástica)")]
        [SerializeField] private GameObject prefabPelotaTenis;

        [Tooltip("Prefab de bola de acero (pesada y contundente)")]
        [SerializeField] private GameObject prefabBolaAcero;

        [Header("Referencias de UI (UnityEngine.UI.Text)")]
        [SerializeField] private Text txtInstrucciones;
        [SerializeField] private Text txtPuntaje;
        [SerializeField] private Text txtMunicion;
        [SerializeField] private Text txtTiros;

        [Header("Referencias de UI alternativas (TextMeshPro)")]
        [SerializeField] private TMP_Text txtInstruccionesTMP;
        [SerializeField] private TMP_Text txtPuntajeTMP;
        [SerializeField] private TMP_Text txtMunicionTMP;
        [SerializeField] private TMP_Text txtTirosTMP;

        [Header("Referencias de Botones UI")]
        [SerializeField] private Button btnCambiarBola;
        [SerializeField] private Button btnResetear;
        [SerializeField] private Button btnReubicar;
        [Tooltip("Botón visible en fase de escaneo para borrar planos y recalcular superficies")]
        [SerializeField] private Button btnRecalcularPlanos;

        [Header("Contenedor de Proyectiles en Vuelo")]
        private readonly List<GameObject> m_ProyectilesActivos = new List<GameObject>();

        private TipoProyectil m_TipoActual = TipoProyectil.Tenis;
        private LataIndividual[] m_LatasEnEscena;
        private int m_LatasDerribadas;
        private int m_TotalLatas = 6;
        private int m_TirosRealizados;

        private float m_TiempoUltimoCambioBola;
        private float m_TiempoUltimoReset;
        private float m_TiempoUltimoReubicar;
        private float m_TiempoUltimoRecalcular;

        // Caché de mensajes de escaneo para evitar asignación continua de strings en Update
        private int m_UltimoConteoPuntosAnim = -1;
        private bool m_UltimoEstadoSuperficie;
        private int m_UltimaCantidadPlanos = -1;

        public TipoProyectil TipoActual => m_TipoActual;

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

            if (controladorAR == null)
            {
                controladorAR = FindFirstObjectByType<ControladorAR>();
            }

            // Input System UI Module
            var uiModule = FindFirstObjectByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (uiModule != null)
            {
                uiModule.AssignDefaultActions();
            }

            // Búsqueda por jerarquía defensiva
            if (btnCambiarBola == null) btnCambiarBola = GameObject.Find("Btn_CambiarBola")?.GetComponent<Button>();
            if (btnResetear == null) btnResetear = GameObject.Find("Btn_Resetear")?.GetComponent<Button>();
            if (btnReubicar == null) btnReubicar = GameObject.Find("Btn_Reubicar")?.GetComponent<Button>();
            if (btnRecalcularPlanos == null) btnRecalcularPlanos = GameObject.Find("Btn_RecalcularPlanos")?.GetComponent<Button>();

            // Respaldo dinámico: si no existe en la escena, creamos el botón a partir del estilo de Btn_Reubicar
            if (btnRecalcularPlanos == null && btnReubicar != null)
            {
                CrearBotonRecalcularDinamico();
            }

            // Vinculación por PointerDown según regla de Cátedra
            RegistrarBoton(btnCambiarBola, AlternarMunicion);
            RegistrarBoton(btnResetear, ReiniciarPiramide);
            RegistrarBoton(btnReubicar, ReubicarEscenario);
            RegistrarBoton(btnRecalcularPlanos, RecalcularPlanos);
        }

        private void CrearBotonRecalcularDinamico()
        {
            GameObject nuevoBotonGO = Instantiate(btnReubicar.gameObject, btnReubicar.transform.parent);
            nuevoBotonGO.name = "Btn_RecalcularPlanos";
            btnRecalcularPlanos = nuevoBotonGO.GetComponent<Button>();

            RectTransform rt = nuevoBotonGO.GetComponent<RectTransform>();
            if (rt != null)
            {
                // Centrado en el panel inferior
                rt.anchorMin = new Vector2(0.20f, 0.15f);
                rt.anchorMax = new Vector2(0.80f, 0.85f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }

            // Cambiamos color para distinguirlo (ámbar/dorado cálido)
            Image img = nuevoBotonGO.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(0.92f, 0.58f, 0.15f, 1.0f);
            }

            // Etiqueta del botón
            Text lbl = nuevoBotonGO.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = "🔄 Recalcular Planos";

            TMP_Text lblTMP = nuevoBotonGO.GetComponentInChildren<TMP_Text>();
            if (lblTMP != null) lblTMP.text = "🔄 Recalcular Planos";
        }

        private void RegistrarBoton(Button btn, UnityEngine.Events.UnityAction accion)
        {
            if (btn == null) return;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(accion);

            EventTrigger trigger = btn.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = btn.gameObject.AddComponent<EventTrigger>();
            }
            trigger.triggers.Clear();

            EventTrigger.Entry entry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerDown
            };
            entry.callback.AddListener((data) => accion());
            trigger.triggers.Add(entry);
        }

        private void Start()
        {
            ActualizarUIMunicion();
            ActualizarUIPuntaje();
            ActualizarUITiros();

            // Al inicio, aseguramos que solo se muestre el botón de recalcular y se oculten los de juego
            ActualizarVisibilidadBotones(EstadoJuego.Escaneando);
            MostrarInstruccion("🔍 Escaneando entorno... Mueve la cámara lentamente hacia una mesa o el suelo.");
        }

        private void Update()
        {
            if (controladorAR == null) return;

            // FASE 1: ESCANEANDO - Feedback visual dinámico del progreso de detección
            if (controladorAR.EstadoActual == EstadoJuego.Escaneando)
            {
                ActualizarFeedbackEscaneo();
            }
            // FASE 2: JUGANDO - Supervisión de latas derribadas y proyectiles
            else if (controladorAR.EstadoActual == EstadoJuego.Jugando)
            {
                ActualizarProgresoJuego();
            }
        }

        private void ActualizarFeedbackEscaneo()
        {
            bool haySuperficie = controladorAR.HaySuperficieBajoMira;
            int cantidadPlanos = controladorAR.CantidadPlanosDetectados;

            if (haySuperficie)
            {
                if (!m_UltimoEstadoSuperficie)
                {
                    m_UltimoEstadoSuperficie = true;
                    MostrarInstruccion("🎯 ¡Superficie detectada! Toca la pantalla para colocar el juego.");
                }
            }
            else
            {
                m_UltimoEstadoSuperficie = false;

                if (cantidadPlanos > 0)
                {
                    if (m_UltimaCantidadPlanos != cantidadPlanos)
                    {
                        m_UltimaCantidadPlanos = cantidadPlanos;
                        MostrarInstruccion($"🔍 Superficies detectadas ({cantidadPlanos}). Apunta la retícula a una mesa o piso.");
                    }
                }
                else
                {
                    m_UltimaCantidadPlanos = 0;
                    // Animación suave de puntos sin asignación excesiva (3 fases)
                    int puntos = ((int)(Time.time * 2.5f) % 3) + 1;
                    if (puntos != m_UltimoConteoPuntosAnim)
                    {
                        m_UltimoConteoPuntosAnim = puntos;
                        string sufijo = puntos == 1 ? "." : (puntos == 2 ? ".." : "...");
                        MostrarInstruccion($"🔍 Buscando superficies planas{sufijo} Mueve el celular apuntando al suelo o mesa.");
                    }
                }
            }
        }

        private void ActualizarProgresoJuego()
        {
            if (m_LatasEnEscena == null || m_LatasEnEscena.Length == 0) return;

            int derribadasAhora = 0;
            for (int i = 0; i < m_LatasEnEscena.Length; i++)
            {
                if (m_LatasEnEscena[i] != null && m_LatasEnEscena[i].HaSidoDerribada)
                {
                    derribadasAhora++;
                }
            }

            if (derribadasAhora != m_LatasDerribadas)
            {
                m_LatasDerribadas = derribadasAhora;
                ActualizarUIPuntaje();

                if (m_LatasDerribadas >= m_TotalLatas)
                {
                    MostrarInstruccion("🏆 ¡Excelente! ¡Derribaste todas las latas! Pulsa 'Resetear' para jugar otra vez.");
                }
            }
        }

        /// <summary>
        /// Controla la visibilidad de los botones según el estado actual del juego.
        /// Durante el escaneo se ocultan Resetear y Cambiar Bola, y se exhibe Recalcular Planos.
        /// </summary>
        public void ActualizarVisibilidadBotones(EstadoJuego estado)
        {
            bool jugando = (estado == EstadoJuego.Jugando);

            if (btnCambiarBola != null) btnCambiarBola.gameObject.SetActive(jugando);
            if (btnResetear != null) btnResetear.gameObject.SetActive(jugando);
            if (btnReubicar != null) btnReubicar.gameObject.SetActive(jugando);

            if (btnRecalcularPlanos != null) btnRecalcularPlanos.gameObject.SetActive(!jugando);
        }

        /// <summary>
        /// Se invoca al instanciarse la pirámide en el mundo real.
        /// Activa la interfaz de juego y los botones de reseteo y munición.
        /// </summary>
        public void RegistrarEstructuraInstanciada(GameObject estructura)
        {
            m_LatasEnEscena = estructura.GetComponentsInChildren<LataIndividual>();
            m_TotalLatas = m_LatasEnEscena.Length;
            m_LatasDerribadas = 0;
            m_TirosRealizados = 0;

            ActualizarUIPuntaje();
            ActualizarUITiros();
            ActualizarVisibilidadBotones(EstadoJuego.Jugando);

            MostrarInstruccion("¡Puesto de tiro listo! Desliza el dedo (swipe) hacia las latas para disparar la bola.");
        }

        /// <summary>
        /// Retorna el prefab del proyectil seleccionado.
        /// </summary>
        public GameObject ObtenerPrefabProyectilActual()
        {
            return m_TipoActual == TipoProyectil.Tenis ? prefabPelotaTenis : prefabBolaAcero;
        }

        /// <summary>
        /// Registra un proyectil disparado.
        /// </summary>
        public void RegistrarDisparo(GameObject proyectil)
        {
            m_TirosRealizados++;
            ActualizarUITiros();

            if (proyectil != null)
            {
                m_ProyectilesActivos.Add(proyectil);
            }
        }

        /// <summary>
        /// Alterna entre Pelota de Tenis y Bola de Acero.
        /// </summary>
        public void AlternarMunicion()
        {
            if (Time.unscaledTime - m_TiempoUltimoCambioBola < 0.25f) return;
            m_TiempoUltimoCambioBola = Time.unscaledTime;

            m_TipoActual = (m_TipoActual == TipoProyectil.Tenis) ? TipoProyectil.Acero : TipoProyectil.Tenis;
            ActualizarUIMunicion();
        }

        /// <summary>
        /// Restablece todas las latas a su posición inicial sobre el pedestal y destruye proyectiles.
        /// </summary>
        public void ReiniciarPiramide()
        {
            if (Time.unscaledTime - m_TiempoUltimoReset < 0.25f) return;
            m_TiempoUltimoReset = Time.unscaledTime;

            // 1. Limpieza de proyectiles en vuelo
            for (int i = m_ProyectilesActivos.Count - 1; i >= 0; i--)
            {
                if (m_ProyectilesActivos[i] != null)
                {
                    Destroy(m_ProyectilesActivos[i]);
                }
            }
            m_ProyectilesActivos.Clear();

            // 2. Restablecer latas sin GC
            if (m_LatasEnEscena != null)
            {
                for (int i = 0; i < m_LatasEnEscena.Length; i++)
                {
                    if (m_LatasEnEscena[i] != null)
                    {
                        m_LatasEnEscena[i].ReiniciarLata();
                    }
                }
            }

            m_LatasDerribadas = 0;
            m_TirosRealizados = 0;
            ActualizarUIPuntaje();
            ActualizarUITiros();
            MostrarInstruccion("¡Latas restablecidas! Desliza el dedo hacia las latas para disparar.");
        }

        /// <summary>
        /// Vuelve al modo de escaneo para situar el juego en otra parte.
        /// </summary>
        public void ReubicarEscenario()
        {
            if (Time.unscaledTime - m_TiempoUltimoReubicar < 0.25f) return;
            m_TiempoUltimoReubicar = Time.unscaledTime;

            ReiniciarPiramide();

            if (controladorAR != null)
            {
                controladorAR.DescongelarYReubicar();
            }

            ActualizarVisibilidadBotones(EstadoJuego.Escaneando);
            m_UltimoEstadoSuperficie = false;
            m_UltimaCantidadPlanos = -1;
            MostrarInstruccion("🔍 Mueve la cámara para buscar una nueva superficie plana y toca para reubicar.");
        }

        /// <summary>
        /// Borra la información previa de planos y reinicia el escaneo desde cero.
        /// Conectado al botón exclusivo de fase de escaneo (Btn_RecalcularPlanos).
        /// </summary>
        public void RecalcularPlanos()
        {
            if (Time.unscaledTime - m_TiempoUltimoRecalcular < 0.25f) return;
            m_TiempoUltimoRecalcular = Time.unscaledTime;

            ReiniciarPiramide();

            if (controladorAR != null)
            {
                controladorAR.RecalcularPlanos();
            }

            ActualizarVisibilidadBotones(EstadoJuego.Escaneando);
            m_UltimoEstadoSuperficie = false;
            m_UltimaCantidadPlanos = -1;
            MostrarInstruccion("🔄 Planos anteriores eliminados. Reiniciando escaneo... Apunta la cámara a una mesa o piso.");
        }

        public void MostrarInstruccion(string mensaje)
        {
            if (txtInstrucciones != null) txtInstrucciones.text = mensaje;
            if (txtInstruccionesTMP != null) txtInstruccionesTMP.text = mensaje;
        }

        private void ActualizarUIPuntaje()
        {
            string texto = $"Latas: {m_LatasDerribadas}/{m_TotalLatas}";
            if (txtPuntaje != null) txtPuntaje.text = texto;
            if (txtPuntajeTMP != null) txtPuntajeTMP.text = texto;
        }

        private void ActualizarUITiros()
        {
            string texto = $"Tiros: {m_TirosRealizados}";
            if (txtTiros != null) txtTiros.text = texto;
            if (txtTirosTMP != null) txtTirosTMP.text = texto;
        }

        private void ActualizarUIMunicion()
        {
            string nombre = (m_TipoActual == TipoProyectil.Tenis) ? "Tenis (Rebotante)" : "Acero (Pesada)";
            string texto = $"Bola: {nombre}";
            if (txtMunicion != null) txtMunicion.text = texto;
            if (txtMunicionTMP != null) txtMunicionTMP.text = texto;
        }
    }
}
