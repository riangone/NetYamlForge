#!/usr/bin/env python3
"""
NetYamlForge AI - CLI Chat Demo
A simple CLI client that talks to the standalone NetYamlForge.AI service.
"""

import sys
import json
import requests
import uuid

BASE_URL = "http://localhost:5005"
PROJECT = "auto-dealer-demo"

def main():
    print("=========================================")
    print("   NetYamlForge AI - CLI Chat Client")
    print("=========================================")
    print(f"Connecting to: {BASE_URL}")
    print(f"Project: {PROJECT}")
    print("Type 'exit' or 'quit' to stop.")
    print("=========================================")

    conv_id = None
    
    # Start a conversation
    try:
        resp = requests.post(f"{BASE_URL}/api/aiwindow/conversations", json={
            "Channel": "cli",
            "Metadata": {"client": "python-cli"}
        })
        resp.raise_for_status()
        data = resp.json()
        conv_id = data["conversationId"]
        print(f"\nAI: {data['welcomeMessage']}")
    except Exception as e:
        print(f"\nError connecting to AI service: {e}")
        print("Make sure the AI service is running at http://localhost:5005")
        sys.exit(1)

    while True:
        try:
            user_input = input("\nYou: ").strip()
            if not user_input:
                continue
            if user_input.lower() in ["exit", "quit"]:
                break

            # Send message
            resp = requests.post(f"{BASE_URL}/api/aiwindow/conversations/{conv_id}/messages", json={
                "Content": user_input,
                "Metadata": {}
            })
            resp.raise_for_status()
            data = resp.json()
            
            print(f"\nAI ({data['aiModel']}): {data['responseText']}")
            
            if data.get("suggestHandover"):
                print("--- [Escalation Suggested] ---")
            
        except KeyboardInterrupt:
            break
        except Exception as e:
            print(f"\nError: {e}")

    print("\nGoodbye!")

if __name__ == "__main__":
    main()
