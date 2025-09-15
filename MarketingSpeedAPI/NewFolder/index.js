// index.js
const express = require("express");
const { Client, LocalAuth } = require("whatsapp-web.js");
const qrcodeTerminal = require("qrcode-terminal");

const app = express();
app.use(express.json());

const sessions = {}; // { sessionId: { client, status, qr } }

// create/start session
app.post("/api/sessions", (req, res) => {
    const { sessionId } = req.body;
    if (!sessionId) return res.status(400).json({ error: "sessionId required" });
    if (sessions[sessionId]) return res.json({ success: true, message: "session exists" });

    const client = new Client({
        authStrategy: new LocalAuth({ clientId: sessionId }),
        puppeteer: { headless: true }
    });

    sessions[sessionId] = { client, status: "initializing", qr: null };

    client.on("qr", (qr) => {
        console.log(`[${sessionId}] QR RECEIVED`);
        // store QR (string)
        sessions[sessionId].qr = qr;
        // also print ascii QR in terminal for convenience
        qrcodeTerminal.generate(qr, { small: true });
    });

    client.on("ready", () => {
        console.log(`[${sessionId}] READY`);
        sessions[sessionId].status = "connected";
        sessions[sessionId].qr = null; // QR no longer needed
    });

    client.on("auth_failure", (msg) => {
        console.log(`[${sessionId}] auth_failure`, msg);
        sessions[sessionId].status = "auth_failure";
    });

    client.on("disconnected", (reason) => {
        console.log(`[${sessionId}] disconnected:`, reason);
        sessions[sessionId].status = "disconnected";
    });

    client.initialize();
    res.json({ success: true, sessionId });
});

// get QR for session
app.get("/api/sessions/:id/qr", (req, res) => {
    const id = req.params.id;
    const s = sessions[id];
    if (!s) return res.status(404).json({ success: false, message: "session not found" });
    if (!s.qr) return res.status(404).json({ success: false, message: "qr not available (maybe already scanned or not generated yet)" });
    // return raw qr string
    res.json({ success: true, qr: s.qr });
});

// get status
app.get("/api/sessions/:id/status", (req, res) => {
    const id = req.params.id;
    const s = sessions[id];
    if (!s) return res.status(404).json({ success: false, message: "session not found" });
    res.json({ success: true, status: s.status });
});

app.post("/api/messages", async (req, res) => {
    const { sessionId, to, message } = req.body;
    const s = sessions[sessionId];
    if (!s || s.status !== "connected") return res.status(400).json({ success: false, message: "session not connected" });
    try {
        await s.client.sendMessage(to.includes("@") ? to : `${to}@c.us`, message);
        res.json({ success: true });
    } catch (err) {
        res.status(500).json({ success: false, error: err.message });
    }
});

app.listen(3000, () => console.log("WhatsApp service running on http://localhost:3000"));
