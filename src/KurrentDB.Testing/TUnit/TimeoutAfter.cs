namespace KurrentDB.Testing.TUnit;

public static class TimeoutAfter {
    public class OneSecondAttribute() : TimeoutAttribute(1000);

    public class FiveSecondsAttribute() : TimeoutAttribute(5000);

    public class TenSecondsAttribute() : TimeoutAttribute(10000);

    public class SixtySecondsAttribute() : TimeoutAttribute(60000);
}
