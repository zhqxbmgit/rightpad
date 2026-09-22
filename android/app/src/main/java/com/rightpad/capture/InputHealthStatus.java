package com.rightpad.capture;

/** Only semantic display values reach the drawing surface. */
record InputHealthStatus(boolean connected, Health input, Health xbox, Health config) {
    enum Health { GOOD, PENDING, ERROR, OFFLINE }
    static final InputHealthStatus OFFLINE = new InputHealthStatus(false,
            Health.OFFLINE, Health.OFFLINE, Health.OFFLINE);
}
