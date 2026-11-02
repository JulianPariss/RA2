#pragma warning disable CS0649

using UnityEngine;
using UnityEngine.UI;

namespace TumbalatasAR
{
    public class VistaLanzadorUI : MonoBehaviour
    {
        [Header("Zona Circular de Disparo")]
        [SerializeField] private RectTransform circuloDisparoRect;
        [SerializeField] private Image circuloDisparoAnillo;
        [SerializeField] private Image circuloDisparoIcono;

        [Header("Medidor de Fuerza")]
        [SerializeField] private RectTransform barraFuerzaContainer;
        [SerializeField] private Image barraFuerzaFill;
        [SerializeField] private Text txtPorcentajeFuerza;

        private Vector3 m_EscalaBaseCirculo = Vector3.one;
        private Color m_ColorAnilloBase = new Color(0.25f, 0.75f, 1.0f, 0.55f);

        private void Awake()
        {
            if (circuloDisparoRect == null)
            {
                var go = GameObject.Find("UI_ZonaDisparo");
                if (go != null)
                {
                    circuloDisparoRect = go.GetComponent<RectTransform>();
                    circuloDisparoAnillo = circuloDisparoRect.Find("AnilloResplandor")?.GetComponent<Image>();
                    circuloDisparoIcono = circuloDisparoRect.Find("IconoBola")?.GetComponent<Image>();
                }
            }

            if (barraFuerzaContainer == null)
            {
                var go = GameObject.Find("UI_BarraFuerza");
                if (go != null)
                {
                    barraFuerzaContainer = go.GetComponent<RectTransform>();
                    barraFuerzaFill = barraFuerzaContainer.Find("RellenoFuerza")?.GetComponent<Image>();
                    txtPorcentajeFuerza = barraFuerzaContainer.Find("Txt_Porcentaje")?.GetComponent<Text>();
                }
            }

            if (circuloDisparoRect != null)
            {
                m_EscalaBaseCirculo = circuloDisparoRect.localScale;
            }

            if (circuloDisparoAnillo != null)
            {
                m_ColorAnilloBase = circuloDisparoAnillo.color;
            }
        }

        public bool PosicionEstaEnZonaDisparo(Vector2 screenPos)
        {
            if (circuloDisparoRect == null || !circuloDisparoRect.gameObject.activeInHierarchy) return false;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(circuloDisparoRect, screenPos, null, out Vector2 localPoint))
            {
                float radio = circuloDisparoRect.rect.width * 0.5f;
                float radioTolerante = radio * 1.20f;
                return localPoint.sqrMagnitude <= (radioTolerante * radioTolerante);
            }

            return false;
        }

        public void ActualizarVisualesCarga(float carga)
        {
            if (barraFuerzaFill != null)
            {
                barraFuerzaFill.fillAmount = carga;

                Color colorFuerza;
                if (carga < 0.5f)
                {
                    colorFuerza = Color.Lerp(new Color(0.2f, 0.85f, 0.4f, 1f), new Color(0.95f, 0.85f, 0.15f, 1f), carga * 2.0f);
                }
                else
                {
                    colorFuerza = Color.Lerp(new Color(0.95f, 0.85f, 0.15f, 1f), new Color(1.0f, 0.22f, 0.18f, 1f), (carga - 0.5f) * 2.0f);
                }
                barraFuerzaFill.color = colorFuerza;

                if (txtPorcentajeFuerza != null)
                {
                    int pct = Mathf.RoundToInt(carga * 100f);
                    txtPorcentajeFuerza.text = pct >= 98 ? "¡MÁX!" : $"{pct}%";
                    txtPorcentajeFuerza.color = colorFuerza;
                }
            }

            if (circuloDisparoRect != null)
            {
                float escala = 1.0f + (carga * 0.16f) + (Mathf.Sin(Time.time * 12f) * 0.02f * carga);
                circuloDisparoRect.localScale = m_EscalaBaseCirculo * escala;
            }

            if (circuloDisparoAnillo != null)
            {
                Color c = Color.Lerp(m_ColorAnilloBase, new Color(1f, 0.9f, 0.25f, 0.95f), carga);
                circuloDisparoAnillo.color = c;
            }
        }

        public void CancelarVisualesCarga()
        {
            if (barraFuerzaFill != null) barraFuerzaFill.fillAmount = 0f;

            if (txtPorcentajeFuerza != null)
            {
                txtPorcentajeFuerza.text = "0%";
                txtPorcentajeFuerza.color = Color.white;
            }

            if (circuloDisparoRect != null)
            {
                circuloDisparoRect.localScale = m_EscalaBaseCirculo;
            }

            if (circuloDisparoAnillo != null)
            {
                circuloDisparoAnillo.color = m_ColorAnilloBase;
            }
        }

        public void ActualizarVisibilidad(bool visible)
        {
            if (circuloDisparoRect != null && circuloDisparoRect.gameObject.activeSelf != visible)
            {
                circuloDisparoRect.gameObject.SetActive(visible);
            }

            if (barraFuerzaContainer != null && barraFuerzaContainer.gameObject.activeSelf != visible)
            {
                barraFuerzaContainer.gameObject.SetActive(visible);
            }
        }

        public void ActualizarVisualTipoMunicion(TipoProyectil tipo)
        {
            if (circuloDisparoIcono == null) return;

            if (tipo == TipoProyectil.Tenis)
            {
                circuloDisparoIcono.color = new Color(0.85f, 0.95f, 0.20f, 0.92f);
            }
            else
            {
                circuloDisparoIcono.color = new Color(0.72f, 0.78f, 0.86f, 0.95f);
            }
        }
    }
}
