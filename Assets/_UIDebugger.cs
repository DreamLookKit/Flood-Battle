using UnityEngine;
using TMPro;

public class _UIDebugger : MonoBehaviour{
    [Header("Links")]
    private Transform playerTransform;
    private Animator anim;
    [SerializeField] private TextMeshProUGUI debugText;
    private Vector3 lastPosition;
    private float horizontalSpeed;
    private float verticalSpeed;
/*     private bool IsInWater;
    private bool IsGrounded;
    private bool IsCrouched; */
    void Update(){
        if (debugText == null) return;
        // Если игрок еще не найден (или раунд перезапустился и старый игрок удален)
        if (playerTransform == null)
        {
            // Ищем на сцене живой объект игрока по его тегу
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            anim = playerObj.GetComponentInChildren<Animator>();
            if (playerObj != null){
                playerTransform = playerObj.transform;
                lastPosition = playerTransform.position;    // Устанавливаем первую координату игрока
            }
            if(anim == null) Debug.LogError("Animator not found!");   // Проверка IsInWater
        }
        // Как только игрок найден — считаем его скорость
        Vector3 currentPosition = playerTransform.position;
        Vector3 displacement = currentPosition - lastPosition;
        float deltaTime = Time.deltaTime;
        if (deltaTime > 0)
        {
            horizontalSpeed = new Vector3(displacement.x, 0f, displacement.z).magnitude / deltaTime;
            verticalSpeed = displacement.y / deltaTime;
        }
        lastPosition = currentPosition;
        // Выводим данные на UI в реальном времени
        /* debugText.text = $"Speed movement (X/Z): {horizontalSpeed:F2}\n" +
                         $"Speed falling (Y): {verticalSpeed:F2}\n" +
                         $"Status Water: {(anim.GetBool("IsInWater") ? "<color=green>Yes</color>" : "<color=white>No</color>")}\n" +
                         $"Status Ground: {(anim.GetBool("IsGrounded") ? "<color=green>Grounded</color>" : "<color=white>In Air?</color>")}\n" +
                         $"Status Crouched: {(anim.GetBool("IsCrouched") ? "<color=green>Crouched</color>" : "<color=white>Stand</color>")}"; */
    }
}
