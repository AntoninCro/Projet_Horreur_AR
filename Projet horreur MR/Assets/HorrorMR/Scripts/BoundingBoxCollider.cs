using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace HorrorMR
{
    /// <summary>Donne au meuble détecté (boîte AR) un collider à sa taille : il bloque la vue du monstre et les balles.</summary>
    [RequireComponent(typeof(ARBoundingBox), typeof(BoxCollider))]
    public class BoundingBoxCollider : MonoBehaviour
    {
        ARBoundingBox m_Box;
        BoxCollider m_Collider;

        void Awake()
        {
            m_Box = GetComponent<ARBoundingBox>();
            m_Collider = GetComponent<BoxCollider>();
            m_Collider.center = Vector3.zero;
        }

        void Update()
        {
            var size = m_Box.size;
            if (size != m_Collider.size)
                m_Collider.size = new Vector3(Mathf.Max(size.x, 0.01f), Mathf.Max(size.y, 0.01f), Mathf.Max(size.z, 0.01f));
        }
    }
}
