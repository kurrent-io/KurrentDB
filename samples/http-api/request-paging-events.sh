#region curl
curl -i http://127.0.0.1:2113/streams/alphabet \
    -H "Accept:application/vnd.kurrent.atom+json"
#endregion curl

#region response
#region responseHeader
HTTP/1.1 200 OK
Access-Control-Allow-Methods: POST, DELETE, GET, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Requested-With, X-Forwarded-Host, X-Forwarded-Prefix, X-PINGOTHER, Authorization, Kurrent-LongPoll, Kurrent-ExpectedVersion, Kurrent-EventId, Kurrent-EventType, Kurrent-RequireLeader, Kurrent-HardDelete, Kurrent-ResolveLinkTos
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: Location, Kurrent-Position, Kurrent-CurrentVersion
Cache-Control: max-age=0, no-cache, must-revalidate
Vary: Accept
ETag: "26;-2060438500"
Content-Type: application/vnd.kurrent.atom+json; charset=utf-8
Server: Kestrel
Date: Tue, 21 Aug 2018 10:12:31 GMT
Content-Length: 10727
Keep-Alive: timeout=15,max=100
#endregion responseHeader

{
  "title": "Event stream 'alphabet'",
  "id": "http://127.0.0.1:2113/streams/alphabet",
  "updated": "2018-08-21T09:53:46.869815Z",
  "streamId": "alphabet",
  "author": {
    "name": "Kurrent"
  },
  "headOfStream": true,
  "selfUrl": "http://127.0.0.1:2113/streams/alphabet",
  "eTag": "26;-2060438500",
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
      "uri": "http://127.0.0.1:2113/streams/alphabet/0/forward/20",
      "relation": "last"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet/6/backward/20",
      "relation": "next"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet/27/forward/20",
      "relation": "previous"
    },
    {
      "uri": "http://127.0.0.1:2113/streams/alphabet/metadata",
      "relation": "metadata"
    }
  ],
  "entries": [
    {
      "title": "26@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/26",
      "updated": "2018-08-21T09:53:46.869815Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/26",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/26",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "25@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/25",
      "updated": "2018-08-21T09:53:46.869811Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/25",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/25",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "24@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/24",
      "updated": "2018-08-21T09:53:46.869809Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/24",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/24",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "23@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/23",
      "updated": "2018-08-21T09:53:46.869806Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/23",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/23",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "22@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/22",
      "updated": "2018-08-21T09:53:46.869804Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/22",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/22",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "21@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/21",
      "updated": "2018-08-21T09:53:46.869802Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/21",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/21",
          "relation": "alternate"
        }
      ]
    },
    {
      "title": "20@alphabet",
      "id": "http://127.0.0.1:2113/streams/alphabet/20",
      "updated": "2018-08-21T09:53:46.869799Z",
      "author": {
        "name": "Kurrent"
      },
      "summary": "event-type",
      "links": [
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/20",
          "relation": "edit"
        },
        {
          "uri": "http://127.0.0.1:2113/streams/alphabet/20",
          "relation": "alternate"
        }
      ]
    },
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
    }
  ]
}
#endregion response
