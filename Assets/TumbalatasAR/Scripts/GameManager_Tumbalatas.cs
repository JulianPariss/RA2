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

    public class GameManager_Tumbalatas : MonoBehaviour
    {
        public static GameManager_Tumbalatas Instance { get; private set; }

        [Header("Controlador Principal")]
        [SerializeField] private ControladorAR controladorAR;

        [Header("Prefabs de Proyectiles")]
        [SerializeField] private GameObject prefabPelotaTenis;
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
        [SerializeField] private Button btnRecalcularPlanos;

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

            var uiModule = FindFirstObjectByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (uiModule != null)
            {
                uiModule.AssignDefaultActions();
            }

            if (btnCambiarBola == null) btnCambiarBola = GameObject.Find("Btn_CambiarBola")?.GetComponent<Button>();
            if (btnResetear == null) btnResetear = GameObject.Find("Btn_Resetear")?.GetComponent<Button>();
            if (btnReubicar == null) btnReubicar = GameObject.Find("Btn_Reubicar")?.GetComponent<Button>();
            if (btnRecalcularPlanos == null) btnRecalcularPlanos = GameObject.Find("Btn_RecalcularPlanos")?.GetComponent<Button>();

            RegistrarBoton(btnCambiarBola, AlternarMunicion);
            RegistrarBoton(btnResetear, ReiniciarPiramide);
            RegistrarBoton(btnReubicar, ReubicarEscenario);
            RegistrarBoton(btnRecalcularPlanos, RecalcularPlanos);
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

            ActualizarVisibilidadBotones(EstadoJuego.Escaneando);
            MostrarInstruccion("Buscando una superficie plana... Apuntá hacia una mesa o al piso.");
        }

        private void Update()
        {
            if (controladorAR == null) return;

            if (controladorAR.EstadoActual == EstadoJuego.Escaneando)
            {
                ActualizarFeedbackEscaneo();
            }
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
                    MostrarInstruccion("¡Superficie encontrada! Tocá la pantalla para ubicar las latas.");
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
                        MostrarInstruccion($"Superficies detectadas ({cantidadPlanos}). Apuntá al piso o a una mesa.");
                    }
                }
                else
                {
                    m_UltimaCantidadPlanos = 0;
                    int puntos = ((int)(Time.time * 2.5f) % 3) + 1;
                    if (puntos != m_UltimoConteoPuntosAnim)
                    {
                        m_UltimoConteoPuntosAnim = puntos;
                        string sufijo = puntos == 1 ? "." : (puntos == 2 ? ".." : "...");
                        MostrarInstruccion($"Buscando superficies{sufijo} Mové el celular apuntando a una mesa o al piso.");
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
                    MostrarInstruccion("¡Tiraste todas las latas! Tocá 'Reiniciar' para jugar de nuevo.");
                }
            }
        }

        public void ActualizarVisibilidadBotones(EstadoJuego estado)
        {
            bool jugando = (estado == EstadoJuego.Jugando);

            if (btnCambiarBola != null) btnCambiarBola.gameObject.SetActive(jugando);
            if (btnResetear != null) btnResetear.gameObject.SetActive(jugando);
            if (btnReubicar != null) btnReubicar.gameObject.SetActive(jugando);

            if (btnRecalcularPlanos != null) btnRecalcularPlanos.gameObject.SetActive(!jugando);

            if (LanzadorProyectiles.Instance != null)
            {
                LanzadorProyectiles.Instance.ActualizarVisibilidadUIDisparo(jugando);
            }
        }

        public void RegistrarEstructuraInstanciada(GameObject estructura)
        {
            m_LatasEnEscena = estructura.GetComponentsInChildren<LataIndividual>();
            m_TotalLatas = m_LatasEnEscena.Length;
            m_LatasDerribadas = 0;
            m_TirosRealizados = 0;

            ActualizarUIPuntaje();
            ActualizarUITiros();
            ActualizarVisibilidadBotones(EstadoJuego.Jugando);

            MostrarInstruccion("¡Listo para jugar! Mantené el círculo para cargar fuerza y deslizá hacia arriba para tirar.");
        }

        public GameObject ObtenerPrefabProyectilActual()
        {
            return m_TipoActual == TipoProyectil.Tenis ? prefabPelotaTenis : prefabBolaAcero;
        }

        public void RegistrarDisparo(GameObject proyectil)
        {
            m_TirosRealizados++;
            ActualizarUITiros();

            if (proyectil != null)
            {
                m_ProyectilesActivos.Add(proyectil);
            }
        }

        public void AlternarMunicion()
        {
            if (Time.unscaledTime - m_TiempoUltimoCambioBola < 0.25f) return;
            m_TiempoUltimoCambioBola = Time.unscaledTime;

            m_TipoActual = (m_TipoActual == TipoProyectil.Tenis) ? TipoProyectil.Acero : TipoProyectil.Tenis;
            ActualizarUIMunicion();

            if (LanzadorProyectiles.Instance != null)
            {
                LanzadorProyectiles.Instance.ActualizarVisualTipoMunicion();
            }
        }

        public void ReiniciarPiramide()
        {
            if (Time.unscaledTime - m_TiempoUltimoReset < 0.25f) return;
            m_TiempoUltimoReset = Time.unscaledTime;

            for (int i = m_ProyectilesActivos.Count - 1; i >= 0; i--)
            {
                if (m_ProyectilesActivos[i] != null)
                {
                    Destroy(m_ProyectilesActivos[i]);
                }
            }
            m_ProyectilesActivos.Clear();

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
            MostrarInstruccion("¡Latas listas! Mantené el círculo para cargar fuerza y deslizá hacia arriba para tirar.");
        }

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
            MostrarInstruccion("Mové la cámara para buscar otra superficie y tocá la pantalla para ubicar las latas.");
        }

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
            MostrarInstruccion("Planos reiniciados. Apuntá la cámara a una mesa o al piso.");
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
            string nombre = (m_TipoActual == TipoProyectil.Tenis) ? "Tenis" : "Acero";
            string texto = $"Pelota: {nombre}";
            if (txtMunicion != null) txtMunicion.text = texto;
            if (txtMunicionTMP != null) txtMunicionTMP.text = texto;
        }
    }
}
