using UnityEngine;
public class PropsSensor : MonoBehaviour
{
    [Header("Prop Detection Settings")]
    [SerializeField] private LayerMask groundLayer;         // Маска слоя Ground
    [SerializeField] private LayerMask waterLayer;          // Маска слоя Water
    [SerializeField] private LayerMask detectionMask;       // Маска слоев (Земля, Вода)
    [SerializeField] private float detectionRadius = 0.2f;  // Радиус сферы детекции внизу объекта
    [SerializeField] private float checkInterval = 0.1f;    // Проверять 10 раз в секунду вместо 50
    public enum PropState { Nothing, Ground, Water }
    public PropState CurrentPropState { get; private set; } = PropState.Nothing;
    private Collider myCollider;
    private readonly Collider[] hitCollidersBottom = new Collider[4];
    private float _nextCheckTime = 0f;
    private void Awake()
    {
    }
    private void Start()
    {
        myCollider = GetComponent<Collider>();
    }
    private void FixedUpdate()
    {
        // Равен ли сенкундомер Time.time нашему интервалу
        if (Time.time >= _nextCheckTime)
        {
            // Передвигаем интервал немного вперед, чтобы тело цикла не срабатывало каждый кадр
            _nextCheckTime = Time.time + checkInterval;
            // Находим слои
            int overlapSphereBottom = Physics.OverlapSphereNonAlloc(
                GetObjectBottom(0.1f), detectionRadius, hitCollidersBottom,
                detectionMask, QueryTriggerInteraction.Collide);
            int accumulatedMask = 0;
            // Если что-то нашли — собираем битовую маску
            if (overlapSphereBottom > 0)
            {
                for (int i = 0; i < overlapSphereBottom; i++)
                {
                    int layer = hitCollidersBottom[i].gameObject.layer;
                    accumulatedMask |= (1 << layer);
                }
            }
            CurrentPropState = ((accumulatedMask & waterLayer.value) != 0) ? PropState.Water :
                               ((accumulatedMask & groundLayer.value) != 0) ? PropState.Ground :
                               PropState.Nothing;
        }
    }
    /*private void OnDrawGizmosSelected(){
        Gizmos.color = CurrentPropState switch{
            PropState.Water     => Color.cyan,
            PropState.Ground    => Color.green,
            PropState.Nothing   => Color.white
        };
        Gizmos.DrawWireSphere(GetObjectBottom(0.1f), detectionRadius);
    }*/
    private Vector3 GetObjectBottom(float correctY)
    {
        if (myCollider != null)
            return new Vector3(transform.position.x, myCollider.bounds.min.y + correctY, transform.position.z);
        return transform.position;
    }
}
