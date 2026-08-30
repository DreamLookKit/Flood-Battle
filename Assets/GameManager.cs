using UnityEngine;

public class GameManager : MonoBehaviour{
    // Экземпляр синглтона (одиночки)
    public static GameManager Instance {get; private set;}
    // Список игровых фаз
    public enum GameState{
        Building, // Фаза 1: Постройка лодки (Например: 2 минуты)
        Flood,    // Фаза 2: Затопление локации
        Battle,   // Фаза 3: Битва с ботами
        RoundEnd  // Фаза 4: Подсчет очков и очистка локации
    }
    // Текущее состояние игры
    public GameState currentState = GameState.Building;
    public float timer = 1f; // Таймер на 10 секунд (для тестов) на постройку лодки
    private void Awake(){
        // Кастомный синглтон: этот объект будет доступен для всех остальных скриптов
        if(Instance == null){
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }else
            Destroy(gameObject);
    }
    // Графика и Логика (Привязана к FPS: 60, 100, 144 - неважно)
    private void Update(){
        // Метод Update вызывается каждый кадр. Уменьшаем таймер
        if (timer > 0){
            timer -= Time.deltaTime; // Time.deltaTime - это задержка между кадрами
        }
        else
            AdvancePhase(); // Таймер закончился, переходим к следующей фазе
    }
    private void AdvancePhase(){
        switch (currentState){
            case GameState.Building:
                currentState = GameState.Flood;
                timer = 30f; // 30 секунд (для тестов) на затопление
                StartFlooding();
                break;
            case GameState.Flood:
                currentState = GameState.Battle;
                timer = 99999f; // 99999 секунд (для тестов) на битву с ботами
                StartBattle();
                break;
            case GameState.Battle:
                currentState = GameState.RoundEnd;
                timer = 20f; // 20 секунд на подсчет очков и очистку локации
                EndRound();
                break;      
        }
    }
    private void StartFlooding(){
        //Debug.Log("Water is rising!"); 
    }
    private void StartBattle(){
        //Debug.Log("Battle started!"); 
    }
    private void EndRound(){
        //Debug.Log("Round ended! Cleaning up..."); 
    }
}
