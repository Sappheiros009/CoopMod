using System.Collections;
using UnityEngine;

public sealed class PairPlayerStartLog :
    MonoBehaviour
{
    private const string LogColor =
        "#FFB347";

    private const float LogDuration =
        8f;

    private static PairPlayerStartLog
        instance;

    private string
        currentDisplayedColoredText =
            "";

    private float
        nativeLogExpireTime =
            0f;

    private Coroutine
        nativeLogCoroutine;

    public static void EnsureCreated()
    {
        if (instance != null)
        {
            return;
        }

        GameObject gameObject =
            new GameObject(
                "PairPlayerStartLog");

        DontDestroyOnLoad(
            gameObject);

        instance =
            gameObject
                .AddComponent<
                    PairPlayerStartLog>();
    }

    private void Awake()
    {
        if (instance != null &&
            instance != this)
        {
            Destroy(
                gameObject);

            return;
        }

        instance =
            this;

        DontDestroyOnLoad(
            gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance =
                null;
        }
    }

    private string
        GetLocalizedMessage()
    {
        switch (
            LocalizedText
                .CURRENT_LANGUAGE)
        {
            case LocalizedText.Language.English:
                return
                    "An even number of players is required to start.";

            case LocalizedText.Language.French:
                return
                    "Un nombre pair de joueurs est requis pour commencer.";

            case LocalizedText.Language.Italian:
                return
                    "È necessario un numero pari di giocatori per iniziare.";

            case LocalizedText.Language.German:
                return
                    "Zum Starten ist eine gerade Anzahl von Spielern erforderlich.";

            case LocalizedText.Language.SpanishSpain:
                return
                    "Se necesita un número par de jugadores para empezar.";

            case LocalizedText.Language.SpanishLatam:
                return
                    "Se necesita un número par de jugadores para comenzar.";

            case LocalizedText.Language.BRPortuguese:
                return
                    "É necessário um número par de jogadores para começar.";

            case LocalizedText.Language.Russian:
                return
                    "Для начала требуется четное количество игроков.";

            case LocalizedText.Language.Ukrainian:
                return
                    "Для початку потрібна парна кількість гравців.";

            case LocalizedText.Language.SimplifiedChinese:
                return
                    "需要偶数名玩家才能开始游戏。";

            case LocalizedText.Language.TraditionalChinese:
                return
                    "需要偶數名玩家才能開始遊戲。";

            case LocalizedText.Language.Japanese:
                return
                    "開始するには偶数人のプレイヤーが必要です。";

            case LocalizedText.Language.Korean:
                return
                    "짝수 플레이어가 있어야 시작할 수 있습니다.";

            case LocalizedText.Language.Polish:
                return
                    "Do rozpoczęcia wymagana jest parzysta liczba graczy.";

            case LocalizedText.Language.Turkish:
                return
                    "Başlamak için çift sayıda oyuncu gereklidir.";

            default:
                return
                    "An even number of players is required to start.";
        }
    }

    public static void
        ShowEvenPlayerRequired()
    {
        EnsureCreated();

        if (instance == null)
        {
            return;
        }

        instance
            .ShowNativeGameLog();
    }

    private void ShowNativeGameLog()
    {
        PlayerConnectionLog playerLog =
            UnityEngine.Object
                .FindFirstObjectByType<
                    PlayerConnectionLog>();

        if (playerLog == null)
        {
            Debug.LogWarning(
                "[PairPlayerStartLog] " +
                "PlayerConnectionLog was not found.");

            return;
        }

        if (playerLog.text == null)
        {
            Debug.LogWarning(
                "[PairPlayerStartLog] " +
                "PlayerConnectionLog text was not found.");

            return;
        }

        string localizedMessage =
            GetLocalizedMessage();

        string coloredMessage =
            "<color=" +
            LogColor +
            ">" +
            localizedMessage +
            "</color>";

        RemoveCurrentLogMessage(
            playerLog);

        currentDisplayedColoredText =
            coloredMessage;

        playerLog.text.text +=
            coloredMessage +
            "\n";

        if (playerLog.sfxJoin)
        {
            playerLog.sfxJoin.Play(
                default(Vector3));
        }
        else
        {
            Debug.LogWarning(
                "[PairPlayerStartLog] " +
                "PEAK join sound was not found.");
        }

        nativeLogExpireTime =
            Time.realtimeSinceStartup +
            LogDuration;

        if (nativeLogCoroutine == null)
        {
            nativeLogCoroutine =
                StartCoroutine(
                    NativeLogTimeoutRoutine());
        }
    }

    private void RemoveCurrentLogMessage(
        PlayerConnectionLog playerLog)
    {
        if (playerLog == null ||
            playerLog.text == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(
            currentDisplayedColoredText))
        {
            return;
        }

        string line =
            currentDisplayedColoredText +
            "\n";

        playerLog.text.text =
            playerLog.text.text.Replace(
                line,
                "");
    }

    private IEnumerator
        NativeLogTimeoutRoutine()
    {
        while (
            Time.realtimeSinceStartup <
            nativeLogExpireTime)
        {
            yield return null;
        }

        PlayerConnectionLog playerLog =
            UnityEngine.Object
                .FindFirstObjectByType<
                    PlayerConnectionLog>();

        if (playerLog != null &&
            playerLog.text != null)
        {
            RemoveCurrentLogMessage(
                playerLog);
        }

        currentDisplayedColoredText =
            "";

        nativeLogCoroutine =
            null;
    }
}
