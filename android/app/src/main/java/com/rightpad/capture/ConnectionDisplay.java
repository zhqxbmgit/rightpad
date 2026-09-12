package com.rightpad.capture;

final class ConnectionDisplay {
    static String title(String address) { return address == null ? "搜索中" : "已连接"; }
    static String address(String address) { return address == null ? "—" : address; }
}
