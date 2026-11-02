#pragma warning disable CS0649

using UnityEngine;
using UnityEngine.InputSystem;

namespace TumbalatasAR
{
    /// <summary>
    /// Componente especializado en la captura de gestos táctiles de swipe / flick,
    /// cálculo de inercia y proyección balística 3D continua de proyectiles hacia las latas.
    /// Compatible con pantalla táctil móvil (Touchscreen) y ratón en Editor / XR Simulator.
    /// Cumple con la regla de Cátedra: Collision Detection = Continuous Dynamic en proyectiles.
    /// </summary>
    public class LanzadorProyectiles : MonoBehaviour
    {
        [Header("Referencias AR")]
        [SerializeField] private Camera arCamera;

        [Header("Ajustes de Lanzamiento Táctil")]
        [Tooltip("Distancia mínima en píxeles para reconocer un gesto de swipe")]
        [SerializeField] private float distanciaMinimaSwipe = 50f;

        [Tooltip("Multiplicador de fuerza según la velocidad del swipe")]
        [SerializeField] private float factorFuerzaLanzamiento = 0.015f;

        [Tooltip("Fuerza mínima de impulso del proyectil")]
        [SerializeField] private float fuerzaMinima = 5.0f;

        [Tooltip("Fuerza máxima de impulso del proyectil")]
        [SerializeField] private float fuerzaMaxima = 22.0f;

        [Tooltip("Tiempo de vida del proyectil antes de ser destruido (segundos)")]
        [SerializeField] private float tiempoVidaProyectil = 6.0f;

        private Vector2 m_SwipeInicioPos;
        private float m_SwipeInicioTiempo;
        private bool m_SwipeEnProgreso;

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }
        }

        private void Update()
        {
            // Solo procesamos el lanzamiento cuando el juego se encuentra en fase activa
            if (ControladorAR.Instance != null && ControladorAR.Instance.EstadoActual == EstadoJuego.Jugando)
            {
                ProcesarEntradaParaLanzamiento();
            }
            else
            {
                m_SwipeEnProgreso = false;
            }
        }

        /// <summary>
        /// Captura el inicio y finalización del gesto de swipe en pantalla libre (fuera de UI).
        /// </summary>
        private void ProcesarEntradaParaLanzamiento()
        {
            Vector2 posicionActual;

            // 1. Inicio del gesto de swipe
            if (ObtenerPulsacionInicial(out posicionActual))
            {
                // Escudo UI: Si el toque inició sobre un botón, se cancela de inmediato
                if (ControladorAR.TocoInterfazDeUsuario(posicionActual))
                {
                    m_SwipeEnProgreso = false;
                    return;
                }

                m_SwipeInicioPos = posicionActual;
                m_SwipeInicioTiempo = Time.time;
                m_SwipeEnProgreso = true;
                return;
            }

            // 2. Finalización del gesto (levantar el dedo o soltar botón del ratón)
            if (m_SwipeEnProgreso && ObtenerPulsacionFinal(out posicionActual))
            {
                m_SwipeEnProgreso = false;

                Vector2 deltaSwipe = posicionActual - m_SwipeInicioPos;
                float distanciaSwipe = deltaSwipe.magnitude;

                // Lanzamiento válido: swipe hacia arriba superando el umbral de distancia
                if (distanciaSwipe >= distanciaMinimaSwipe && deltaSwipe.y > 0)
                {
                    float deltaTiempo = Mathf.Max(Time.time - m_SwipeInicioTiempo, 0.05f);
                    float velocidadPantalla = distanciaSwipe / deltaTiempo;

                    EjecutarLanzamientoProyectil(deltaSwipe, velocidadPantalla);
                }
            }
        }

        /// <summary>
        /// Convierte el vector del swipe 2D en una fuerza 3D combinada con la orientación de la cámara AR.
        /// </summary>
        private void EjecutarLanzamientoProyectil(Vector2 deltaSwipe, float velocidadPantalla)
        {
            if (GameManager_Tumbalatas.Instance == null || arCamera == null) return;

            GameObject prefabProyectil = GameManager_Tumbalatas.Instance.ObtenerPrefabProyectilActual();
            if (prefabProyectil == null) return;

            // Punto de spawn: ligeramente al frente y por debajo del visor de la cámara
            Vector3 origen = arCamera.transform.position + (arCamera.transform.forward * 0.25f) - (arCamera.transform.up * 0.05f);

            // Vector de dirección 3D combinando orientación de la cámara con la dirección del swipe
            float desvioX = (deltaSwipe.x / Screen.width) * 1.6f;
            float inclinacionY = Mathf.Clamp((deltaSwipe.y / Screen.height) * 1.8f, 0.2f, 1.5f);

            Vector3 direccion = (arCamera.transform.forward * 1.2f + arCamera.transform.up * inclinacionY + arCamera.transform.right * desvioX).normalized;

            // Cálculo de fuerza acotada entre límites de seguridad
            float fuerza = Mathf.Clamp(velocidadPantalla * factorFuerzaLanzamiento, fuerzaMinima, fuerzaMaxima);

            // Instanciamos el proyectil
            GameObject proyectil = Instantiate(prefabProyectil, origen, Quaternion.identity);
            Rigidbody rb = proyectil.GetComponent<Rigidbody>();

            if (rb != null)
            {
                // Regla de cátedra: Continuous Dynamic en proyectiles rápidos para evitar efecto túnel
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.linearVelocity = direccion * fuerza;
                rb.angularVelocity = arCamera.transform.right * 12.0f; // Giro rotacional realista
            }

            GameManager_Tumbalatas.Instance.RegistrarDisparo(proyectil);

            Destroy(proyectil, tiempoVidaProyectil);
        }

        // =========================================================================
        // ENTRADA MULTIPLATAFORMA (TOUCHSCREEN + MOUSE / POINTER)
        // =========================================================================

        private bool ObtenerPulsacionInicial(out Vector2 posicion)
        {
            posicion = Vector2.zero;

            // 1. Pantalla táctil en móvil Android
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                posicion = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            // 2. Mouse en Editor / XR Simulator
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                posicion = Mouse.current.position.ReadValue();
                return true;
            }

            // 3. Fallback genérico Pointer
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                posicion = Pointer.current.position.ReadValue();
                return true;
            }

            return false;
        }

        private bool ObtenerPulsacionFinal(out Vector2 posicion)
        {
            posicion = Vector2.zero;

            // 1. Pantalla táctil en móvil Android
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                posicion = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            // 2. Mouse en Editor / XR Simulator
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                posicion = Mouse.current.position.ReadValue();
                return true;
            }

            // 3. Fallback genérico Pointer
            if (Pointer.current != null && Pointer.current.press.wasReleasedThisFrame)
            {
                posicion = Pointer.current.position.ReadValue();
                return true;
            }

            return false;
        }
    }
}
