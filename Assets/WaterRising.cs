using UnityEngine;

public class WaterRising : MonoBehaviour{
    [SerializeField] private float modificationSpeed = 0.5f; // Как быстро растет объем
    [SerializeField] private float maxScaleY = 5.0f; // Максимальная толщина/высота воды
    [SerializeField] private float minScaleY = 1.0f; // Минимальная толщина/высота воды
    [SerializeField] private float declineSpeedMultiplier = 3f; // Множитель скорости убывания уровня воды
    // Экземпляр синглтона (одиночки)
    public static WaterRising Instance { get; private set; }
    // Это свойство возвращает точную верхнюю точку объекта воды в мире.
    public float SurfaceY => transform.position.y + (transform.localScale.y / 2f);
    // Публичное свойство для безопасного изменения maxScaleY из других скриптов
    public float MaxScaleY 
    {
        get => maxScaleY;
        set => maxScaleY = Mathf.Max(minScaleY, value); // Блокировка изменения максимума, если он меньше минимума
    }
    private void Awake(){
        // Кастомный синглтон: этот объект будет доступен для всех остальных скриптов
        if(Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    // Физическая сила всегда применяется в FixedUpdate (50 раз всекунду)
    private void FixedUpdate(){
        // Вычисляем, насколько должен измениться масштаб в этом кадре
        float scaleChange = modificationSpeed * Time.fixedDeltaTime;
        // Безопасная проверка фазы игры через синглтон
        if (GameManager.Instance != null){
            switch (GameManager.Instance.currentState){
                case GameManager.GameState.Flood:
                    GrowWaterVolume(scaleChange);
                    break;
                case GameManager.GameState.RoundEnd:
                    DeclineWaterVolume(scaleChange);
                    break;
            }
        } 
    }
    private void GrowWaterVolume(float scaleChange){
        // Проверяем, находится ли толщина воды все еще ниже максимального предела
        if(transform.localScale.y < maxScaleY){
            // 1. Вычисляем, насколько должен увеличиться масштаб в этом кадре
            // float scaleChange = modificationSpeed * Time.deltaTime;
            // 2. Увеличиваем масштаб по оси Y (растягиваем объем)
            transform.localScale += new Vector3(0, scaleChange, 0);
            // 3. Перемещаем объект вверх на половину изменения масштаба.
            // Это закрепляет нижнюю часть куба и заставляет его расти только вверх.
            transform.position += new Vector3(0, scaleChange / 2f, 0);
        }
    }
    private void DeclineWaterVolume(float scaleChange){
        // Проверяем, находится ли толщина воды все еще выше минимального предела
        if(transform.localScale.y > minScaleY){
            // 1. Вычисляем, насколько должен уменьшиться масштаб в этом кадре
            // float scaleChange = modificationSpeed * Time.deltaTime;
            // 2. Уменьшаем масштаб по оси Y (сужаем объем)
            transform.localScale -= new Vector3(0, scaleChange * declineSpeedMultiplier, 0);
            // 3. Перемещаем объект вниз на половину изменения масштаба.
            // Это закрепляет нижнюю часть куба и заставляет его уменьшаться только вниз.
            transform.position -= new Vector3(0, (scaleChange * declineSpeedMultiplier) / 2f, 0);
            // Если масштаб воды случайно упал ниже минимального уровня, жестко фиксируем его
            if(transform.localScale.y < minScaleY)
            transform.localScale = new Vector3(transform.localScale.x, minScaleY, transform.localScale.z);
        }
    }
}

