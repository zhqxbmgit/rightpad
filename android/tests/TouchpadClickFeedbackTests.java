package com.rightpad.capture;

public final class TouchpadClickFeedbackTests {
    private static int checks;
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); checks++; }
    private static final class Fake implements TouchpadClickFeedback.Backend {
        int confirms;
        boolean performed = true, fail;
        public boolean confirm() {
            confirms++;
            if (fail) throw new IllegalStateException();
            return performed;
        }
    }
    public static void main(String[] args) throws Exception {
        Fake f = new Fake(); TouchpadClickFeedback feedback = new TouchpadClickFeedback(f);
        check(feedback.acceptedClick(), "accepted execution");
        check(f.confirms == 1, "accepted click exactly one CONFIRM");
        f.fail = true;
        check(!feedback.acceptedClick(), "failed backend contained without retry");
        check(f.confirms == 2, "no fallback or stronger retry");
        f.fail = false; f.performed = false;
        check(!feedback.acceptedClick(), "system declined feedback");
        check(f.confirms == 3, "declined feedback not retried");
        f.performed = true;
        check(feedback.acceptedClick() && f.confirms == 4, "next accepted click survives prior failures");
        System.out.println("RESULT TouchpadClickFeedbackTests checks=" + checks + " failed=0");
    }
}
