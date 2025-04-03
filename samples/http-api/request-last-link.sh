#region curl
curl -i http://127.0.0.1:2113/streams/alphabet/0/forward/20 \
    -H "Accept:application/vnd.kurrent.atom+json"
#endregion curl

#region response
HTTP/1.1 200 OK
Access-Control-Allow-Methods: GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Cache-Control: max-age=31536000, public
Vary: Accept
Content-Type: application/vnd.kurrent.atom+json; charset=utf-8
Server: Kestrel
Date: Tue, 21 Aug 2018 10:24:28 GMT
Content-Length: 10403
Keep-Alive: timeout=15,max=100

{
  "title": "Event stream 'alphabet'",
  "id": "http://127.0.0.1:2113/streams/alphabet",
  "updated": "2018-08-21T09:53:46.869716Z",
  "streamId": "alphabet",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": false,
  "links": [
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet",
      "relation": "self"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet/head/backward/20",
      "relation": "first"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet/20/forward/20",
      "relation": "previous"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet/metadata",
      "relation": "metadata"
    }
  ],
  "entries": [
    {
      "title": "19@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/19",
      "updated": "2018-08-21T09:53:46.869791Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/19",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/19",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "18@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/18",
      "updated": "2018-08-21T09:53:46.869788Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/18",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/18",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "17@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/17",
      "updated": "2018-08-21T09:53:46.869786Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/17",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/17",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "16@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/16",
      "updated": "2018-08-21T09:53:46.869782Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/16",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/16",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "15@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/15",
      "updated": "2018-08-21T09:53:46.86978Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/15",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/15",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "14@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/14",
      "updated": "2018-08-21T09:53:46.869778Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/14",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/14",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "13@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/13",
      "updated": "2018-08-21T09:53:46.869773Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/13",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/13",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "12@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/12",
      "updated": "2018-08-21T09:53:46.869771Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/12",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/12",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "11@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/11",
      "updated": "2018-08-21T09:53:46.869769Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/11",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/11",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "10@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/10",
      "updated": "2018-08-21T09:53:46.869766Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/10",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/10",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "9@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/9",
      "updated": "2018-08-21T09:53:46.869764Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/9",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/9",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "8@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/8",
      "updated": "2018-08-21T09:53:46.86976Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/8",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/8",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "7@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/7",
      "updated": "2018-08-21T09:53:46.869758Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/7",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/7",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "6@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/6",
      "updated": "2018-08-21T09:53:46.869755Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/6",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/6",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "5@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/5",
      "updated": "2018-08-21T09:53:46.869753Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/5",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/5",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "4@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/4",
      "updated": "2018-08-21T09:53:46.86975Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/4",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/4",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "3@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/3",
      "updated": "2018-08-21T09:53:46.869748Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/3",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/3",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "2@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/2",
      "updated": "2018-08-21T09:53:46.869746Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/2",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/2",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "1@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/1",
      "updated": "2018-08-21T09:53:46.869742Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/1",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/1",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "0@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/0",
      "updated": "2018-08-21T09:53:46.869716Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/0",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/0",
          "relation": "alternate"
        }
      ]
    }
  ]
}
#endregion response
