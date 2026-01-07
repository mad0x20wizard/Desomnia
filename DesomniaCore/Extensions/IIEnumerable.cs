namespace System.Collections.Generic
{
    public interface IIEnumerable<out T> : IEnumerable<T>
    {
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
