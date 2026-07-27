namespace TradingBot.Research;

public static class ResearchExitCode
{
    public const int Success = 0;
    public const int AcceptanceRejected = 3;

    public static int FromAcceptance(bool isAccepted) =>
        isAccepted ? Success : AcceptanceRejected;
}
