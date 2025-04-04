using System.IO;

namespace EventStore.Transport.Http.EntityManagement {
	public interface IHttpResponse {
		void AddHeader(string name, string value);
		void Close();
		long ContentLength64 { get; set; }
		string ContentType { get; set; }
		Stream OutputStream { get; }
		int StatusCode { get; set; }
		string StatusDescription { get; set; }
	}
}
