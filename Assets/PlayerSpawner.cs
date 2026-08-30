using UnityEngine;

public class PlayerSpawner : MonoBehaviour{
    [Header("Spawn settings")]
    [SerializeField] private GameObject playerPrefab;  // Прераб вашего игрока
    [SerializeField] private Transform spawnPoint;     // Точка спавна вашего игрока
    private GameObject currentInstance;
    private void Awake(){
        SpawnPlayer();  // Спавним игрока при появлении сцены
    }
    public void SpawnPlayer(){
        // Проверяем, не забыли ли мы перетащить объекты в Инспекторе
        if(playerPrefab == null || spawnPoint == null){
            Debug.LogError("Player: Prefab or SpawnPoint not assigned in Spawner!");
            return;
        }
        // Если копия игрока уже была на сцене (для будущих возрождений), удаляем старую
        if(currentInstance != null)
            Destroy(currentInstance);
        // Создаем игрока из префаба в точке подключения с требуемым поворотом
        currentInstance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        Debug.Log("Player has appear");
    }
}
